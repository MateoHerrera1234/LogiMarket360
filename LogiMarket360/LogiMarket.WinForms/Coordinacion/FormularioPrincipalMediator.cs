using System;
using System.Threading.Tasks;

namespace LogiMarket.WinForms.Coordinacion;

// Coordina el refresco de las vistas afectadas por operaciones de negocio sin acoplarlas entre sí.
public sealed class FormularioPrincipalMediator
{
    private readonly Func<Task> _refrescarCatalogo;
    private readonly Func<Task> _refrescarClientes;
    private readonly Func<Task> _refrescarPedidos;
    private readonly Func<Task> _refrescarAuditoria;
    private readonly Action _refrescarBorrador;
    private readonly Action _refrescarMetricas;

    public FormularioPrincipalMediator(
        Func<Task> refrescarCatalogo,
        Func<Task> refrescarClientes,
        Func<Task> refrescarPedidos,
        Func<Task> refrescarAuditoria,
        Action refrescarBorrador,
        Action refrescarMetricas)
    {
        _refrescarCatalogo = refrescarCatalogo ?? throw new ArgumentNullException(nameof(refrescarCatalogo));
        _refrescarClientes = refrescarClientes ?? throw new ArgumentNullException(nameof(refrescarClientes));
        _refrescarPedidos = refrescarPedidos ?? throw new ArgumentNullException(nameof(refrescarPedidos));
        _refrescarAuditoria = refrescarAuditoria ?? throw new ArgumentNullException(nameof(refrescarAuditoria));
        _refrescarBorrador = refrescarBorrador ?? throw new ArgumentNullException(nameof(refrescarBorrador));
        _refrescarMetricas = refrescarMetricas ?? throw new ArgumentNullException(nameof(refrescarMetricas));
    }

    public async Task NotificarOperacionActualizadaAsync()
    {
        await _refrescarCatalogo();
        await _refrescarClientes();
        await _refrescarPedidos();
        await _refrescarAuditoria();
        _refrescarBorrador();
        _refrescarMetricas();
    }
}
