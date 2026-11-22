using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LogiMarket.Application.Comportamentales;
using LogiMarket.Application.Creacionales;
using LogiMarket.Application.Estructurales;
using LogiMarket.Application.Puertos;
using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Enums;
using LogiMarket.Domain.Pedidos;
using LogiMarket.Infrastructure.Documentos;
using LogiMarket.Infrastructure.Notificaciones;
using LogiMarket.Infrastructure.Pagos;
using LogiMarket.Infrastructure.Persistencia;

namespace LogiMarket.WinForms.Coordinacion;

// Coordinador de presentacion que ensambla Application e Infrastructure sin meter reglas de dominio en la UI
public sealed class CoordinadorLogiMarketWinForms : IDisposable
{
    public ContextoSqliteLogiMarket ContextoSqlite { get; }
    public IRepositorioCatalogo Catalogo { get; }
    public IRepositorioClientes Clientes { get; }
    public IRepositorioPedidos Pedidos { get; }
    public IRepositorioAuditoriaOutbox AuditoriaOutbox { get; }
    public IMetricasSesion Metricas { get; }
    public ProveedorPasarelaHeredado ProveedorPago { get; }
    public IPasarelaPago PasarelaPago { get; }
    public PublicadorEventosSistema PublicadorEventos { get; }
    public InvocadorComandos Invocador { get; }
    public BorradorPedidoOriginator Borrador { get; }
    public HistorialBorradorCaretaker HistorialBorrador { get; }
    public FabricaMetadatosVisuales MetadatosVisuales { get; }
    public ISalidaDocumento SalidaComprobante { get; }

    public CoordinadorLogiMarketWinForms(string? rutaBaseDatos = null)
    {
        ContextoSqlite = new ContextoSqliteLogiMarket(rutaBaseDatos);
        Catalogo = new RepositorioCatalogoSqlite(ContextoSqlite);
        Clientes = new RepositorioClientesSqlite(ContextoSqlite);
        Pedidos = new RepositorioPedidosSqlite(ContextoSqlite);
        AuditoriaOutbox = new RepositorioAuditoriaOutboxSqlite(ContextoSqlite);
        Metricas = MetricasSesion.Instancia;

        ProveedorPago = new ProveedorPasarelaHeredado();
        var adaptador = new PasarelaPagoAdapter(ProveedorPago);
        var proxy = new PagoAutorizadoProxy(adaptador, ContextoSqlite);
        PasarelaPago = new AuditoriaPagoDecorator(proxy, AuditoriaOutbox, ContextoSqlite);

        PublicadorEventos = new PublicadorEventosSistema();
        PublicadorEventos.Suscribir(new ObservadorMetricasSesion(Metricas));
        PublicadorEventos.Suscribir(new ObservadorAuditoriaEvento(AuditoriaOutbox, ContextoSqlite));
        var creadorNotif = new CreadorNotificadorArchivo();
        PublicadorEventos.Suscribir(new ObservadorNotificacionAviso(creadorNotif.CrearNotificador()));

        Invocador = new InvocadorComandos();
        Borrador = new BorradorPedidoOriginator();
        HistorialBorrador = new HistorialBorradorCaretaker();
        MetadatosVisuales = new FabricaMetadatosVisuales();
        SalidaComprobante = new SalidaArchivoComprobante();

        ContextoSqlite.IniciarSesion(new UsuarioSesion(
            Guid.Parse("11111111-1111-1111-1111-111111111101"),
            "admin",
            "Administrador General",
            RolUsuario.ADMIN));
    }

    public Task InicializarAsync(CancellationToken ct = default) =>
        ContextoSqlite.InicializarEsquemaAsync(ct);

    public void CambiarRolDemo(RolUsuario nuevoRol)
    {
        string usuario = nuevoRol switch
        {
            RolUsuario.ADMIN => "admin",
            RolUsuario.VENTAS => "vendedor1",
            RolUsuario.BODEGA => "bodega1",
            _ => "consulta1"
        };

        ContextoSqlite.IniciarSesion(new UsuarioSesion(Guid.NewGuid(), usuario, $"Operador {nuevoRol}", nuevoRol));
    }

    public async Task<Pedido> ConstruirPedidoDesdeBorradorAsync(Cliente cliente, string codigoPedido, CancellationToken ct = default)
    {
        if (cliente is null)
            throw new ArgumentNullException(nameof(cliente));

        var calculadora = new CalculadoraDescuentos(new IPoliticaDescuento[]
        {
            new PoliticaMayorista15(),
            new PoliticaVip10(),
            new PoliticaSinDescuento()
        });

        var builder = new PedidoBuilder(calculadora)
            .ConCodigo(codigoPedido)
            .ParaCliente(cliente)
            .ConTipoEntrega(Borrador.TipoEntrega);

        foreach (var item in Borrador.Items)
        {
            var elemento = await Catalogo.ObtenerPorSkuAsync(item.Sku, ct)
                ?? throw new InvalidOperationException($"No se encontro el elemento {item.Sku} en el catalogo");

            builder.AgregarElemento(elemento, item.Cantidad);
        }

        return builder.Construir();
    }

    public async Task<ResultadoComando> ConfirmarPedidoActualAsync(Cliente cliente, string codigoPedido, string claveIdempotencia, CancellationToken ct = default)
    {
        var pedido = await ConstruirPedidoDesdeBorradorAsync(cliente, codigoPedido, ct);
        var cadena = EnsambladorCadenaValidacion.CrearCadenaConfirmacion(Clientes, Catalogo, ContextoSqlite);
        var fachada = new ConfirmacionPedidoFacade(
            ContextoSqlite,
            Pedidos,
            Catalogo,
            AuditoriaOutbox,
            ContextoSqlite,
            PasarelaPago,
            Metricas);

        var comando = new ConfirmarPedidoCommand(pedido, claveIdempotencia, cadena, fachada, PublicadorEventos);
        var resultado = await Invocador.EjecutarAsync(comando, ct);
        if (resultado.Exitoso)
            HistorialBorrador.LimpiarAlConfirmar();

        return resultado;
    }

    public async Task<IReadOnlyList<Pedido>> FiltrarPedidosAsync(string? expresionFiltro, CancellationToken ct = default)
    {
        var todos = await Pedidos.ListarTodosAsync(ct);
        var expresion = ParserFiltroPedidos.Parsear(expresionFiltro);
        return todos.Where(p => expresion.Evaluar(p)).ToList().AsReadOnly();
    }

    public void Dispose() => ContextoSqlite.Dispose();
}
