using System;
using System.IO;
using System.Threading.Tasks;
using LogiMarket.Application.Creacionales;
using LogiMarket.Application.CasosUso;
using LogiMarket.Application.Comportamentales;
using LogiMarket.Application.Estructurales;
using LogiMarket.Application.Puertos;
using LogiMarket.Domain.Pedidos;
using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Enums;
using LogiMarket.Infrastructure.Persistencia;
using LogiMarket.Infrastructure.Pagos;
using Xunit;

namespace LogiMarket.Tests;

public sealed class PruebasIntegracionSqlite : IAsyncLifetime
{
    private readonly string _rutaDb = Path.Combine(Path.GetTempPath(), $"logimarket_test_{Guid.NewGuid():N}.db");
    private ContextoSqliteLogiMarket _contexto = null!;

    public async Task InitializeAsync()
    {
        _contexto = new ContextoSqliteLogiMarket(_rutaDb);
        await _contexto.InicializarEsquemaAsync();
    }

    public Task DisposeAsync()
    {
        _contexto.Dispose();
        if (File.Exists(_rutaDb))
        {
            try { File.Delete(_rutaDb); } catch { /* ignorar archivo bloqueado */ }
        }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RepositoriosSqlite_DebenPersistirYRecuperarProductoYCliente()
    {
        var repoCatalogo = new RepositorioCatalogoSqlite(_contexto);
        var repoClientes = new RepositorioClientesSqlite(_contexto);

        var prod = new ProductoSimple("SKU-INT-1", "Caja Integracion", 35.00m, 1.5m, 15);
        await repoCatalogo.GuardarProductoAsync(prod);

        var cli = new Cliente(Guid.NewGuid(), "CLI-INT-1", "Cliente Integracion", "int@test.ec", CategoriaCliente.MAYORISTA, true);
        await repoClientes.GuardarAsync(cli);

        var prodLeido = await repoCatalogo.ObtenerPorSkuAsync("SKU-INT-1");
        var cliLeido = await repoClientes.ObtenerPorIdAsync(cli.Id);

        Assert.NotNull(prodLeido);
        Assert.Equal(15, prodLeido!.StockDisponible);
        Assert.NotNull(cliLeido);
        Assert.Equal("CLI-INT-1", cliLeido!.Codigo);
    }

    [Fact]
    public async Task Seed_DebeCrearCatalogoOficialYSerIdempotente()
    {
        var repo = new RepositorioCatalogoSqlite(_contexto);
        var primero = await repo.ListarTodosAsync();
        await _contexto.InicializarEsquemaAsync();
        var segundo = await repo.ListarTodosAsync();

        Assert.Equal(7, primero.Count);
        Assert.Equal(7, segundo.Count);
        Assert.Equal(8, segundo.Single(x => x.Sku == "P01").StockDisponible);
        Assert.Equal(5, segundo.Single(x => x.Sku == "P02").StockDisponible);
        Assert.Equal(20, segundo.Single(x => x.Sku == "P03").StockDisponible);
        Assert.Equal(50m, segundo.Single(x => x.Sku == "KIT_OFICINA").PrecioUnitario);
        Assert.Equal(60m, segundo.Single(x => x.Sku == "KIT_TELETRABAJO").PrecioUnitario);
    }

    [Fact]
    public async Task Stock_ActualizacionConcurrenteNoPermiteVenderMasUnidadesDeLasDisponibles()
    {
        var catalogo = new RepositorioCatalogoSqlite(_contexto);
        await catalogo.GuardarProductoAsync(new ProductoSimple("SKU-CONC-1", "Producto concurrente", 10m, .1m, 8));

        async Task<bool> IntentarVenderSeisAsync()
        {
            try
            {
                await catalogo.ActualizarStockAsync("SKU-CONC-1", 2, -6, "PRUEBA_CONCURRENCIA", Guid.NewGuid().ToString("N"));
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        bool[] resultados = await Task.WhenAll(IntentarVenderSeisAsync(), IntentarVenderSeisAsync());

        Assert.Single(resultados.Where(x => x));
        Assert.Equal(2, (await catalogo.ObtenerPorSkuAsync("SKU-CONC-1"))!.StockDisponible);
        var movimientos = await new RepositorioAuditoriaOutboxSqlite(_contexto).ListarMovimientosInventarioAsync();
        Assert.Single(movimientos.Where(x => x.Sku == "SKU-CONC-1"));
    }

    [Fact]
    public async Task AccesoLocal_DebeVerificarHashYRechazarContrasenaIncorrecta()
    {
        Assert.False(await _contexto.IniciarSesionAsync("admin", "incorrecta"));
        Assert.Null(_contexto.UsuarioActual);

        Assert.True(await _contexto.IniciarSesionAsync("admin", "Admin-LM360-2026!"));
        Assert.Equal(RolUsuario.ADMIN, _contexto.UsuarioActual!.Rol);

        _contexto.CerrarSesion();
        Assert.Null(_contexto.UsuarioActual);
    }

    [Fact]
    public async Task Outbox_ConservaFalloYReintentaHastaProcesarElEvento()
    {
        Assert.True(await _contexto.IniciarSesionAsync("admin", "Admin-LM360-2026!"));
        var repositorio = new RepositorioAuditoriaOutboxSqlite(_contexto);
        await repositorio.AgregarEventoOutboxAsync(
            "PedidoConfirmado",
            "{\"Codigo\":\"PED-OUTBOX\",\"NombreCliente\":\"Cliente\",\"Total\":15,\"ReferenciaPasarela\":\"REF-1\"}",
            "OUTBOX-TEST-REINTENTO");

        var observador = new ObservadorConFalloTransitorio();
        var publicador = new PublicadorEventosSistema();
        publicador.Suscribir(observador);
        var procesador = new ProcesadorOutbox(repositorio, _contexto, publicador);

        var primerIntento = await procesador.ProcesarPendientesAsync();
        var pendiente = Assert.Single(await repositorio.ObtenerEventosOutboxPendientesAsync());
        Assert.Equal(1, primerIntento.Fallidos);
        Assert.Equal(1, pendiente.Intentos);

        observador.Fallar = false;
        var segundoIntento = await procesador.ProcesarPendientesAsync();

        Assert.Equal(1, segundoIntento.Procesados);
        Assert.Empty(await repositorio.ObtenerEventosOutboxPendientesAsync());
    }

    [Fact]
    public async Task Confirmacion_ClaveIdempotenciaDevuelvePagoOriginalSinRepetirDescuentoStock()
    {
        Assert.True(await _contexto.IniciarSesionAsync("vendedor1", "Ventas-LM360-2026!"));
        var catalogo = new RepositorioCatalogoSqlite(_contexto);
        var clientes = new RepositorioClientesSqlite(_contexto);
        var pedidos = new RepositorioPedidosSqlite(_contexto);
        var auditoria = new RepositorioAuditoriaOutboxSqlite(_contexto);
        var calculadora = new CalculadoraDescuentos(new IPoliticaDescuento[]
        {
            new PoliticaMayorista15(), new PoliticaVip10(), new PoliticaSinDescuento()
        });
        var cliente = await clientes.ObtenerPorCodigoAsync("CLI-ANA");
        var kit = await catalogo.ObtenerPorSkuAsync("KIT_TELETRABAJO");
        var pedido = new PedidoBuilder(calculadora)
            .ConId(Guid.Parse("33333333-3333-3333-3333-333333333301"))
            .ConCodigo("PED-IDEMPOTENCIA")
            .ParaCliente(cliente!)
            .AgregarElemento(kit!, 1)
            .Construir();
        var adaptador = new PasarelaPagoAdapter(new ProveedorPasarelaHeredado());
        var decorador = new AuditoriaPagoDecorator(adaptador, auditoria, _contexto);
        var proxy = new PagoAutorizadoProxy(decorador, _contexto);
        var metricas = new MetricasSesion();
        var facade = new ConfirmacionPedidoFacade(_contexto, pedidos, catalogo, auditoria, _contexto, proxy, metricas);

        var primera = await facade.ConfirmarAsync(pedido, "IDEM-PED-1");
        var repetida = await facade.ConfirmarAsync(pedido, "IDEM-PED-1");
        var movimientos = await auditoria.ListarMovimientosInventarioAsync();

        Assert.True(primera.Exitoso);
        Assert.True(repetida.Exitoso);
        Assert.Equal(primera.ReferenciaPago, repetida.ReferenciaPago);
        Assert.Equal(59m, repetida.TotalCobrado);
        Assert.Equal(3, movimientos.Count);
        Assert.Equal(7, (await catalogo.ObtenerPorSkuAsync("P01"))!.StockDisponible);
        Assert.Equal(4, (await catalogo.ObtenerPorSkuAsync("P02"))!.StockDisponible);
        Assert.Equal(19, (await catalogo.ObtenerPorSkuAsync("P03"))!.StockDisponible);
    }

    private sealed class ObservadorConFalloTransitorio : IObservadorEventoSistema
    {
        public bool Fallar { get; set; } = true;

        public Task AlRecibirEventoAsync(EventoSistemaLogiMarket evento, System.Threading.CancellationToken ct = default)
        {
            if (Fallar)
                throw new InvalidOperationException("Fallo transitorio simulado");
            return Task.CompletedTask;
        }
    }
}
