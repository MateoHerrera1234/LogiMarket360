using System;
using System.Collections.Generic;
using System.Linq;
using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Enums;

namespace LogiMarket.Domain.Pedidos;

// Instantanea inmutable de cada componente fisico expandido dentro de una linea de pedido
public sealed record InstantaneaComponenteLinea(string SkuFisico, int CantidadPorUnidadLinea, int CantidadTotalFisica);

// Linea de pedido que conserva instantanea de precio y composicion de kits al momento de registro
public sealed class LineaPedido
{
    private readonly List<InstantaneaComponenteLinea> _componentesExpandidos = new();

    public Guid Id { get; private set; }
    public string Sku { get; private set; }
    public string NombreProducto { get; private set; }
    public bool EsKit { get; private set; }
    public int Cantidad { get; private set; }
    public decimal PrecioUnitarioInstantanea { get; private set; }
    public decimal SubtotalLinea => decimal.Round(PrecioUnitarioInstantanea * Cantidad, 2, MidpointRounding.AwayFromZero);
    public IReadOnlyList<InstantaneaComponenteLinea> ComponentesExpandidos => _componentesExpandidos.AsReadOnly();

    public LineaPedido(IElementoCatalogo elemento, int cantidad)
    {
        if (elemento is null)
            throw new ArgumentNullException(nameof(elemento));

        if (cantidad <= 0)
            throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad debe ser mayor a cero");

        Id = Guid.NewGuid();
        Sku = elemento.Sku;
        NombreProducto = elemento.Nombre;
        EsKit = elemento is KitCompuesto;
        Cantidad = cantidad;
        PrecioUnitarioInstantanea = elemento.PrecioUnitario;

        var expansionUnitaria = elemento.ExpandirCantidadesPorSku(1);
        foreach (var par in expansionUnitaria)
        {
            _componentesExpandidos.Add(new InstantaneaComponenteLinea(
                par.Key,
                par.Value,
                par.Value * cantidad));
        }
    }

    public LineaPedido(
        Guid id,
        string sku,
        string nombreProducto,
        bool esKit,
        int cantidad,
        decimal precioUnitarioInstantanea,
        IEnumerable<InstantaneaComponenteLinea> componentes)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        Sku = sku;
        NombreProducto = nombreProducto;
        EsKit = esKit;
        Cantidad = cantidad;
        PrecioUnitarioInstantanea = precioUnitarioInstantanea;
        _componentesExpandidos.AddRange(componentes);
    }
}

// Agregado raiz Pedido que protege invariantes monetarias y ciclo de vida con State
public sealed class Pedido
{
    private readonly List<LineaPedido> _lineas = new();
    private IEstadoPedido _estadoActual;

    public Guid Id { get; private set; }
    public string Codigo { get; private set; }
    public Guid ClienteId { get; private set; }
    public string NombreCliente { get; private set; }
    public CategoriaCliente CategoriaCliente { get; private set; }
    public TipoEntrega TipoEntrega { get; private set; }
    public decimal TarifaEnvio { get; private set; }
    public int PlazoEntregaDias { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal Descuento { get; private set; }
    public string PoliticaDescuentoAplicada { get; private set; } = "NORMAL_0";
    public decimal Total { get; private set; }
    public EstadoPedido Estado => _estadoActual.Estado;
    public DateTime FechaCreacionUtc { get; private set; }
    public DateTime? FechaEntregaUtc { get; private set; }
    public bool TieneDevolucionRegistrada { get; private set; }
    public IReadOnlyList<LineaPedido> Lineas => _lineas.AsReadOnly();

    public Pedido(
        Guid id,
        string codigo,
        Guid clienteId,
        string nombreCliente,
        CategoriaCliente categoriaCliente,
        TipoEntrega tipoEntrega,
        decimal tarifaEnvio,
        int plazoEntregaDias,
        IEnumerable<LineaPedido> lineas,
        CalculadoraDescuentos calculadoraDescuentos)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            throw new ArgumentException("El codigo del pedido es obligatorio", nameof(codigo));

        if (clienteId == Guid.Empty)
            throw new ArgumentException("El cliente es obligatorio", nameof(clienteId));

        if (string.IsNullOrWhiteSpace(nombreCliente))
            throw new ArgumentException("El nombre del cliente es obligatorio", nameof(nombreCliente));

        if (tarifaEnvio < 0m)
            throw new ArgumentOutOfRangeException(nameof(tarifaEnvio), "La tarifa de envio no puede ser negativa");

        if (plazoEntregaDias <= 0)
            throw new ArgumentOutOfRangeException(nameof(plazoEntregaDias), "El plazo de entrega debe ser positivo");

        var listaLineas = lineas?.ToList() ?? throw new ArgumentNullException(nameof(lineas));
        if (listaLineas.Count == 0)
            throw new InvalidOperationException("Un pedido debe contener al menos una linea");

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        Codigo = codigo.Trim().ToUpperInvariant();
        ClienteId = clienteId;
        NombreCliente = nombreCliente.Trim();
        CategoriaCliente = categoriaCliente;
        TipoEntrega = tipoEntrega;
        TarifaEnvio = decimal.Round(tarifaEnvio, 2, MidpointRounding.AwayFromZero);
        PlazoEntregaDias = plazoEntregaDias;
        _lineas.AddRange(listaLineas);
        _estadoActual = new EstadoPedidoBorrador();
        FechaCreacionUtc = DateTime.UtcNow;

        RecalcularTotales(calculadoraDescuentos);
    }

    public int CalcularTotalUnidadesFisicasFinales() =>
        _lineas.SelectMany(l => l.ComponentesExpandidos).Sum(c => c.CantidadTotalFisica);

    public IReadOnlyDictionary<string, int> ObtenerRequerimientosStockPorSku()
    {
        var totales = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var comp in _lineas.SelectMany(l => l.ComponentesExpandidos))
        {
            if (totales.TryGetValue(comp.SkuFisico, out int existente))
                totales[comp.SkuFisico] = existente + comp.CantidadTotalFisica;
            else
                totales[comp.SkuFisico] = comp.CantidadTotalFisica;
        }

        return totales;
    }

    public decimal CalcularImporteReembolsableProductos() =>
        decimal.Round(Subtotal - Descuento, 2, MidpointRounding.AwayFromZero);

    public void ReemplazarLineas(IEnumerable<LineaPedido> nuevasLineas, CalculadoraDescuentos calculadora)
    {
        if (!_estadoActual.PermiteEdicion)
            throw new InvalidOperationException($"No se permite editar un pedido en estado {Estado}");

        var lista = nuevasLineas?.ToList() ?? throw new ArgumentNullException(nameof(nuevasLineas));
        if (lista.Count == 0)
            throw new InvalidOperationException("El pedido debe conservar al menos una linea");

        _lineas.Clear();
        _lineas.AddRange(lista);
        RecalcularTotales(calculadora);
    }

    public void Validar() => _estadoActual = _estadoActual.Validar();

    public void MarcarComoPagado() => _estadoActual = _estadoActual.Pagar();

    public void MarcarComoDespachado() => _estadoActual = _estadoActual.Despachar();

    public void MarcarComoEntregado(DateTime fechaEntregaUtc)
    {
        _estadoActual = _estadoActual.Entregar();
        FechaEntregaUtc = fechaEntregaUtc;
    }

    public void Cancelar() => _estadoActual = _estadoActual.Cancelar();

    public void MarcarDevolucionRegistrada()
    {
        if (Estado != EstadoPedido.ENTREGADO)
            throw new InvalidOperationException("Solo se puede devolver un pedido ENTREGADO");

        if (TieneDevolucionRegistrada)
            throw new InvalidOperationException("El pedido ya tiene una devolucion registrada");

        TieneDevolucionRegistrada = true;
    }

    private void RecalcularTotales(CalculadoraDescuentos calculadora)
    {
        if (calculadora is null)
            throw new ArgumentNullException(nameof(calculadora));

        Subtotal = decimal.Round(_lineas.Sum(l => l.SubtotalLinea), 2, MidpointRounding.AwayFromZero);
        int unidadesFisicas = CalcularTotalUnidadesFisicasFinales();
        var (politica, descuentoCalculado) = calculadora.Evaluar(Subtotal, CategoriaCliente, unidadesFisicas);
        PoliticaDescuentoAplicada = politica.NombrePolitica;
        Descuento = decimal.Round(descuentoCalculado, 2, MidpointRounding.AwayFromZero);
        Total = decimal.Round(Subtotal - Descuento + TarifaEnvio, 2, MidpointRounding.AwayFromZero);
    }
}
