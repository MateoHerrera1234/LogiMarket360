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
