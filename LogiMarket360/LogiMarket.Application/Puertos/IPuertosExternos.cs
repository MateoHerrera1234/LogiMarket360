using System;
using System.Threading;
using System.Threading.Tasks;

namespace LogiMarket.Application.Puertos;

// Resultado contractual del cobro simulado
public sealed record ResultadoCobro(bool Exitoso, string ReferenciaTransaccion, string Proveedor, string Mensaje);

// Puerto de pasarela de pago simulada usado por Proxy, Decorator y Adapter
public interface IPasarelaPago
{
    string NombreProveedor { get; }
    Task<ResultadoCobro> CobrarAsync(decimal montoUsd, string claveIdempotencia, CancellationToken ct = default);
}

// Puerto de salida de documentos usado por el patron Bridge
public interface ISalidaDocumento
{
    string NombreMedio { get; }
    Task<string> EmitirAsync(string titulo, string contenido, CancellationToken ct = default);
}

// Puerto de notificacion usado por el patron Factory Method
public interface INotificadorAviso
{
    string Canal { get; }
    Task EnviarAsync(string destinatario, string asunto, string mensaje, CancellationToken ct = default);
}
