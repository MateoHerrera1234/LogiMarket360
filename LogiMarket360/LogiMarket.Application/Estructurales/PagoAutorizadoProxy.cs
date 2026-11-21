using System;
using System.Threading;
using System.Threading.Tasks;
using LogiMarket.Application.Puertos;
using LogiMarket.Domain.Enums;

namespace LogiMarket.Application.Estructurales;

// Proxy que verifica el rol autenticado antes de delegar cobros a la pasarela real
public sealed class PagoAutorizadoProxy : IPasarelaPago
{
    private readonly IPasarelaPago _pasarelaReal;
    private readonly IContextoSesion _contextoSesion;

    public string NombreProveedor => _pasarelaReal.NombreProveedor;

    public PagoAutorizadoProxy(IPasarelaPago pasarelaReal, IContextoSesion contextoSesion)
    {
        _pasarelaReal = pasarelaReal ?? throw new ArgumentNullException(nameof(pasarelaReal));
        _contextoSesion = contextoSesion ?? throw new ArgumentNullException(nameof(contextoSesion));
    }

    public Task<ResultadoCobro> CobrarAsync(decimal montoUsd, string claveIdempotencia, CancellationToken ct = default)
    {
        if (!_contextoSesion.TienePermiso(RolUsuario.VENTAS, RolUsuario.ADMIN))
        {
            string rolActual = _contextoSesion.UsuarioActual?.Rol.ToString() ?? "SIN_SESION";
            return Task.FromResult(new ResultadoCobro(
                false,
                string.Empty,
                NombreProveedor,
                $"Acceso denegado: el rol {rolActual} no tiene permiso para ejecutar cobros"));
        }

        return _pasarelaReal.CobrarAsync(montoUsd, claveIdempotencia, ct);
    }
}
