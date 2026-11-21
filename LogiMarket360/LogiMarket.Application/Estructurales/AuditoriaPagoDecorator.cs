using System;
using System.Threading;
using System.Threading.Tasks;
using LogiMarket.Application.Puertos;

namespace LogiMarket.Application.Estructurales;

// Decorador que registra trazabilidad de cobros con identificador de correlacion sin alterar el resultado
public sealed class AuditoriaPagoDecorator : IPasarelaPago
{
    private readonly IPasarelaPago _pasarelaInterna;
    private readonly IRepositorioAuditoriaOutbox _auditoria;
    private readonly IContextoSesion _sesion;

    public string NombreProveedor => _pasarelaInterna.NombreProveedor;

    public AuditoriaPagoDecorator(
        IPasarelaPago pasarelaInterna,
        IRepositorioAuditoriaOutbox auditoria,
        IContextoSesion sesion)
    {
        _pasarelaInterna = pasarelaInterna ?? throw new ArgumentNullException(nameof(pasarelaInterna));
        _auditoria = auditoria ?? throw new ArgumentNullException(nameof(auditoria));
        _sesion = sesion ?? throw new ArgumentNullException(nameof(sesion));
    }

    public async Task<ResultadoCobro> CobrarAsync(decimal montoUsd, string claveIdempotencia, CancellationToken ct = default)
    {
        string correlacionId = Guid.NewGuid().ToString("N")[..12];
        string usuario = _sesion.UsuarioActual?.NombreUsuario ?? "SISTEMA";

        await _auditoria.RegistrarAuditoriaAsync(
            "PAGO",
            "INICIO_COBRO",
            $"Proveedor={NombreProveedor}; Monto={montoUsd:0.00}; Idempotencia={claveIdempotencia}",
            usuario,
            correlacionId,
            ct);

        try
        {
            var resultado = await _pasarelaInterna.CobrarAsync(montoUsd, claveIdempotencia, ct);

            await _auditoria.RegistrarAuditoriaAsync(
                "PAGO",
                resultado.Exitoso ? "COBRO_APROBADO" : "COBRO_RECHAZADO",
                $"Ref={resultado.ReferenciaTransaccion}; Detalle={resultado.Mensaje}",
                usuario,
                correlacionId,
                ct);

            return resultado;
        }
        catch (Exception ex)
        {
            await _auditoria.RegistrarAuditoriaAsync(
                "PAGO",
                "COBRO_ERROR",
                $"Error={ex.Message}; Idempotencia={claveIdempotencia}",
                usuario,
                correlacionId,
                ct);

            throw;
        }
    }
}
