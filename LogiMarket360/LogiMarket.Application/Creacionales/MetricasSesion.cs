using System;
using System.Threading;

namespace LogiMarket.Application.Creacionales;

// Contrato para consultar y actualizar metricas compartidas de sesion
public interface IMetricasSesion
{
    int PedidosConfirmadosSesion { get; }
    int CobrosRechazadosSesion { get; }
    int DevolucionesProcesadasSesion { get; }
    decimal TotalFacturadoSesionUsd { get; }
    DateTime UltimaActualizacionUtc { get; }

    void RegistrarPedidoConfirmado(decimal montoUsd);
    void RegistrarCobroRechazado();
    void RegistrarDevolucionProcesada();
    void ReiniciarContadores();
}

// Implementacion del patron Singleton thread-safe compartida entre formularios y servicios
public sealed class MetricasSesion : IMetricasSesion
{
    private static readonly Lazy<MetricasSesion> _instancia = new(() => new MetricasSesion(), LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly object _bloqueo = new();

    private int _pedidosConfirmados;
    private int _cobrosRechazados;
    private int _devolucionesProcesadas;
    private decimal _totalFacturado;
    private DateTime _ultimaActualizacionUtc = DateTime.UtcNow;

    public static MetricasSesion Instancia => _instancia.Value;

    public MetricasSesion()
    {
    }

    public int PedidosConfirmadosSesion
    {
        get { lock (_bloqueo) return _pedidosConfirmados; }
    }

    public int CobrosRechazadosSesion
    {
        get { lock (_bloqueo) return _cobrosRechazados; }
    }

    public int DevolucionesProcesadasSesion
    {
        get { lock (_bloqueo) return _devolucionesProcesadas; }
    }

    public decimal TotalFacturadoSesionUsd
    {
        get { lock (_bloqueo) return _totalFacturado; }
    }

    public DateTime UltimaActualizacionUtc
    {
        get { lock (_bloqueo) return _ultimaActualizacionUtc; }
    }

    public void RegistrarPedidoConfirmado(decimal montoUsd)
    {
        lock (_bloqueo)
        {
            _pedidosConfirmados++;
            _totalFacturado += montoUsd;
            _ultimaActualizacionUtc = DateTime.UtcNow;
        }
    }

    public void RegistrarCobroRechazado()
    {
        lock (_bloqueo)
        {
            _cobrosRechazados++;
            _ultimaActualizacionUtc = DateTime.UtcNow;
        }
    }

    public void RegistrarDevolucionProcesada()
    {
        lock (_bloqueo)
        {
            _devolucionesProcesadas++;
            _ultimaActualizacionUtc = DateTime.UtcNow;
        }
    }

    public void ReiniciarContadores()
    {
        lock (_bloqueo)
        {
            _pedidosConfirmados = 0;
            _cobrosRechazados = 0;
            _devolucionesProcesadas = 0;
            _totalFacturado = 0m;
            _ultimaActualizacionUtc = DateTime.UtcNow;
        }
    }
}
