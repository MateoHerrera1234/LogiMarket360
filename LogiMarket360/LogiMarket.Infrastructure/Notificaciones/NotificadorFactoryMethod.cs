using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LogiMarket.Application.Puertos;

namespace LogiMarket.Infrastructure.Notificaciones;

// Implementacion concreta: notificacion por archivo local de registro
public sealed class NotificadorArchivoLocal : INotificadorAviso
{
    private readonly string _rutaArchivo;

    public string Canal => "ARCHIVO";

    public NotificadorArchivoLocal(string? rutaArchivo = null)
    {
        if (string.IsNullOrWhiteSpace(rutaArchivo))
        {
            string carpeta = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LogiMarket360");
            Directory.CreateDirectory(carpeta);
            _rutaArchivo = Path.Combine(carpeta, "notificaciones.log");
        }
        else
        {
            _rutaArchivo = rutaArchivo;
        }
    }

    public async Task EnviarAsync(string destinatario, string asunto, string mensaje, CancellationToken ct = default)
    {
        string linea = $"[{DateTime.UtcNow:O}] A={destinatario} | ASUNTO={asunto} | MSG={mensaje}{Environment.NewLine}";
        await File.AppendAllTextAsync(_rutaArchivo, linea, ct);
    }
}

// Implementacion concreta: notificacion en memoria/diagnostico para pruebas o vista rapida
public sealed class NotificadorMemoriaDiagnostico : INotificadorAviso
{
    private readonly List<string> _mensajesEnviados = new();

    public string Canal => "MEMORIA";
    public IReadOnlyList<string> MensajesEnviados => _mensajesEnviados.AsReadOnly();

    public Task EnviarAsync(string destinatario, string asunto, string mensaje, CancellationToken ct = default)
    {
        _mensajesEnviados.Add($"[{Canal}] {destinatario}: {asunto} -> {mensaje}");
        return Task.CompletedTask;
    }
}

// Canal EMAIL simulado: registra el mensaje en un archivo local sin enviar correo externo.
public sealed class NotificadorEmailSimulado : INotificadorAviso
{
    private readonly NotificadorArchivoLocal _registro;
    public string Canal => "EMAIL";
    public NotificadorEmailSimulado(string? ruta = null) => _registro = new NotificadorArchivoLocal(ruta ?? RutaCanal("email"));
    public Task EnviarAsync(string destinatario, string asunto, string mensaje, CancellationToken ct = default) =>
        _registro.EnviarAsync($"EMAIL:{destinatario}", asunto, mensaje, ct);

    internal static string RutaCanal(string canal) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogiMarket360", $"notificaciones-{canal}.log");
}

// Canal SMS simulado: registra el mensaje en un archivo local sin enviar SMS externo.
public sealed class NotificadorSmsSimulado : INotificadorAviso
{
    private readonly NotificadorArchivoLocal _registro;
    public string Canal => "SMS";
    public NotificadorSmsSimulado(string? ruta = null) => _registro = new NotificadorArchivoLocal(ruta ?? NotificadorEmailSimulado.RutaCanal("sms"));
    public Task EnviarAsync(string destinatario, string asunto, string mensaje, CancellationToken ct = default) =>
        _registro.EnviarAsync($"SMS:{destinatario}", asunto, mensaje, ct);
}

// Creador abstracto del patron Factory Method
public abstract class CreadorNotificadorAviso
{
    public abstract INotificadorAviso CrearNotificador();

    public Task NotificarAsync(string destinatario, string asunto, string mensaje, CancellationToken ct = default) =>
        CrearNotificador().EnviarAsync(destinatario, asunto, mensaje, ct);
}

public sealed class CreadorNotificadorArchivo : CreadorNotificadorAviso
{
    private readonly string? _ruta;
    public CreadorNotificadorArchivo(string? ruta = null) => _ruta = ruta;
    public override INotificadorAviso CrearNotificador() => new NotificadorArchivoLocal(_ruta);
}

public sealed class CreadorNotificadorMemoria : CreadorNotificadorAviso
{
    private readonly NotificadorMemoriaDiagnostico _instancia;
    public CreadorNotificadorMemoria(NotificadorMemoriaDiagnostico? instancia = null) =>
        _instancia = instancia ?? new NotificadorMemoriaDiagnostico();
    public override INotificadorAviso CrearNotificador() => _instancia;
}

public sealed class CreadorNotificadorEmail : CreadorNotificadorAviso
{
    private readonly string? _ruta;
    public CreadorNotificadorEmail(string? ruta = null) => _ruta = ruta;
    public override INotificadorAviso CrearNotificador() => new NotificadorEmailSimulado(_ruta);
}

public sealed class CreadorNotificadorSms : CreadorNotificadorAviso
{
    private readonly string? _ruta;
    public CreadorNotificadorSms(string? ruta = null) => _ruta = ruta;
    public override INotificadorAviso CrearNotificador() => new NotificadorSmsSimulado(_ruta);
}
