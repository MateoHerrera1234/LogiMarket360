using System;
using System.Threading;
using System.Threading.Tasks;
using LogiMarket.Application.Puertos;
using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Enums;

namespace LogiMarket.Application.CasosUso;

// Límite de autorización para cambios de catálogo y clientes, independiente de WinForms.
public sealed class ServicioAdministracionCatalogoClientes
{
    private readonly IContextoSesion _sesion;
    private readonly IRepositorioCatalogo _catalogo;
    private readonly IRepositorioClientes _clientes;

    public ServicioAdministracionCatalogoClientes(IContextoSesion sesion, IRepositorioCatalogo catalogo, IRepositorioClientes clientes)
    {
        _sesion = sesion ?? throw new ArgumentNullException(nameof(sesion));
        _catalogo = catalogo ?? throw new ArgumentNullException(nameof(catalogo));
        _clientes = clientes ?? throw new ArgumentNullException(nameof(clientes));
    }

    public Task GuardarProductoAsync(ProductoSimple producto, CancellationToken ct = default)
    {
        Exigir(RolUsuario.ADMIN);
        return _catalogo.GuardarProductoAsync(producto, ct);
    }

    public Task GuardarKitAsync(KitCompuesto kit, CancellationToken ct = default)
    {
        Exigir(RolUsuario.ADMIN);
        return _catalogo.GuardarKitAsync(kit, ct);
    }

    public Task GuardarClienteAsync(Cliente cliente, CancellationToken ct = default)
    {
        Exigir(RolUsuario.ADMIN, RolUsuario.VENTAS);
        return _clientes.GuardarAsync(cliente, ct);
    }

    private void Exigir(params RolUsuario[] roles)
    {
        if (!_sesion.TienePermiso(roles))
        {
            string rol = _sesion.UsuarioActual?.Rol.ToString() ?? "SIN_SESION";
            throw new UnauthorizedAccessException($"El rol {rol} no tiene permiso para esta operación.");
        }
    }
}
