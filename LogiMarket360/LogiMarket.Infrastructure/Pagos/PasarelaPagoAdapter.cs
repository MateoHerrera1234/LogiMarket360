using System;
using System.Threading;
using System.Threading.Tasks;
using LogiMarket.Application.Puertos;

namespace LogiMarket.Infrastructure.Pagos;

// Proveedor simulado heredado con firma distinta (CobrarUsd) que requiere Adapter hacia IPasarelaPago
public sealed class ProveedorPasarelaHeredado
{
    public bool SimularFallo { get; set; }

    public (bool Aprobado, string CodigoAutorizacion, string Detalle) CobrarUsd(decimal importe, string referenciaUnica)
    {
        if (SimularFallo)
            return (false, string.Empty, "Fondos insuficientes o rechazo simulado del proveedor");

        if (importe <= 0m)
            return (false, string.Empty, "Importe invalido para cobro");

        string authCode = $"AUTH-{Math.Abs(referenciaUnica.GetHashCode()):X8}";
        return (true, authCode, $"Cobro aprobado por ${importe:0.00}");
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
