using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LogiMarket.Application.Creacionales;
using LogiMarket.Application.Puertos;
using LogiMarket.Domain.Enums;

namespace LogiMarket.Application.Comportamentales;

// Evento de dominio/aplicacion notificado tras confirmar una operacion relevante
public sealed record EventoSistemaLogiMarket(
    string TipoEvento,
    string CodigoReferencia,
    string Destinatario,
    decimal MontoUsd,
    string Detalle,
    DateTime OcurridoUtc);

// Contrato del patron Observer para suscriptores de eventos de negocio
public interface IObservadorEventoSistema
{
    Task AlRecibirEventoAsync(EventoSistemaLogiMarket evento, CancellationToken ct = default);
}

// Contrato del patron Mediator para coordinar solicitudes o notificaciones sin acoplamiento directo
public interface IMediadorOperaciones
{
    Task PublicarEventoAsync(EventoSistemaLogiMarket evento, CancellationToken ct = default);
}

// Publicador Observer que difunde eventos a todos los observadores suscritos
public sealed class PublicadorEventosSistema : IMediadorOperaciones
{
    private readonly List<IObservadorEventoSistema> _observadores = new();

    public void Suscribir(IObservadorEventoSistema observador)
    {
        if (observador is not null && !_observadores.Contains(observador))
            _observadores.Add(observador);
    }

    public void Desuscribir(IObservadorEventoSistema observador)
    {
        if (observador is not null)
            _observadores.Remove(observador);
    }

    public async Task PublicarEventoAsync(EventoSistemaLogiMarket evento, CancellationToken ct = default)
    {
        if (evento is null)
            throw new ArgumentNullException(nameof(evento));

        foreach (var observador in _observadores.ToArray())
        {
            await observador.AlRecibirEventoAsync(evento, ct);
        }
    }
}

// Suscriptor 1: actualiza el Singleton de metricas de sesion
public sealed class ObservadorMetricasSesion : IObservadorEventoSistema
{
    private readonly IMetricasSesion _metricas;

    public ObservadorMetricasSesion(IMetricasSesion metricas)
    {
        _metricas = metricas ?? throw new ArgumentNullException(nameof(metricas));
    }

    public Task AlRecibirEventoAsync(EventoSistemaLogiMarket evento, CancellationToken ct = default)
    {
        if (evento.TipoEvento == "DEVOLUCION_PROCESADA")
            _metricas.RegistrarDevolucionProcesada();

        return Task.CompletedTask;
    }
}

// Suscriptor 2: registra actividad en el puerto de auditoria
public sealed class ObservadorAuditoriaEvento : IObservadorEventoSistema
{
    private readonly IRepositorioAuditoriaOutbox _auditoria;
    private readonly IContextoSesion _sesion;

    public ObservadorAuditoriaEvento(IRepositorioAuditoriaOutbox auditoria, IContextoSesion sesion)
    {
        _auditoria = auditoria ?? throw new ArgumentNullException(nameof(auditoria));
        _sesion = sesion ?? throw new ArgumentNullException(nameof(sesion));
    }

    public Task AlRecibirEventoAsync(EventoSistemaLogiMarket evento, CancellationToken ct = default)
    {
        string usuario = _sesion.UsuarioActual?.NombreUsuario ?? "SISTEMA";
        return _auditoria.RegistrarAuditoriaAsync(
            "EVENTO",
            evento.TipoEvento,
            $"{evento.CodigoReferencia}: {evento.Detalle}",
            usuario,
            null,
            ct);
    }
}

// Suscriptor 3: envia aviso por el canal configurado (Factory Method / INotificadorAviso)
public sealed class ObservadorNotificacionAviso : IObservadorEventoSistema
{
    private readonly INotificadorAviso _notificador;

    public ObservadorNotificacionAviso(INotificadorAviso notificador)
    {
        _notificador = notificador ?? throw new ArgumentNullException(nameof(notificador));
    }

    public Task AlRecibirEventoAsync(EventoSistemaLogiMarket evento, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(evento.Destinatario))
            return Task.CompletedTask;

        return _notificador.EnviarAsync(
            evento.Destinatario,
            $"[LogiMarket 360] {evento.TipoEvento} - {evento.CodigoReferencia}",
            evento.Detalle,
            ct);
    }
}
