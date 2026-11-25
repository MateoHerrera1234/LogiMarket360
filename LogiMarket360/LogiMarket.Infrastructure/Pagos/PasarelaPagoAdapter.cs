using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using LogiMarket.Application.Puertos;

namespace LogiMarket.Infrastructure.Pagos;

// Proveedor simulado heredado con firma distinta (CobrarUsd) que requiere Adapter hacia IPasarelaPago
public sealed class ProveedorPasarelaHeredado
{
    private readonly ConcurrentDictionary<string, (decimal Importe, bool Aprobado, string Codigo, string Detalle)> _operaciones = new(StringComparer.Ordinal);
    public bool SimularFallo { get; set; }

    public (bool Aprobado, string CodigoAutorizacion, string Detalle) CobrarUsd(decimal importe, string referenciaUnica)
    {
        if (string.IsNullOrWhiteSpace(referenciaUnica))
            return (false, string.Empty, "La referencia idempotente es obligatoria");

        var operacion = _operaciones.GetOrAdd(referenciaUnica, clave =>
        {
            if (SimularFallo)
                return (importe, false, string.Empty, "Fondos insuficientes o rechazo simulado del proveedor");
            if (importe <= 0m)
                return (importe, false, string.Empty, "Importe invalido para cobro");

            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clave)))[..8];
            return (importe, true, $"AUTH-{hash}", $"Cobro aprobado por ${importe:0.00}");
        });

        if (operacion.Importe != importe)
            return (false, string.Empty, "La clave idempotente ya fue usada con otro importe");
        return (operacion.Aprobado, operacion.Codigo, operacion.Detalle);
    }
}

// Adaptador que traduce el proveedor heredado hacia el contrato IPasarelaPago de Application
public sealed class PasarelaPagoAdapter : IPasarelaPago
{
    private readonly ProveedorPasarelaHeredado _proveedor;

    public string NombreProveedor => "PASARELA_SIMULADA_ADAPTER";

    public PasarelaPagoAdapter(ProveedorPasarelaHeredado proveedor)
    {
        _proveedor = proveedor ?? throw new ArgumentNullException(nameof(proveedor));
    }

    public Task<ResultadoCobro> CobrarAsync(decimal montoUsd, string claveIdempotencia, CancellationToken ct = default)
    {
        var (aprobado, codigoAuth, detalle) = _proveedor.CobrarUsd(montoUsd, claveIdempotencia);
        var resultado = new ResultadoCobro(aprobado, codigoAuth, NombreProveedor, detalle);
        return Task.FromResult(resultado);
    }
}
