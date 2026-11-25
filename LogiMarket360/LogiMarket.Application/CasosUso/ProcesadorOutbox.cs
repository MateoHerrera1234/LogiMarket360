using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LogiMarket.Application.Comportamentales;
using LogiMarket.Application.Puertos;
using LogiMarket.Domain.Enums;

namespace LogiMarket.Application.CasosUso;

public sealed record ResultadoProcesamientoOutbox(int Procesados, int Fallidos);

// Publica efectos posteriores solo desde eventos durables y conserva los fallidos para reintento.
public sealed class ProcesadorOutbox
{
    private readonly IRepositorioAuditoriaOutbox _repositorio;
    private readonly IContextoSesion _sesion;
    private readonly PublicadorEventosSistema _publicador;

    public ProcesadorOutbox(IRepositorioAuditoriaOutbox repositorio, IContextoSesion sesion, PublicadorEventosSistema publicador)
    {
        _repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        _sesion = sesion ?? throw new ArgumentNullException(nameof(sesion));
        _publicador = publicador ?? throw new ArgumentNullException(nameof(publicador));
    }

    public async Task<ResultadoProcesamientoOutbox> ProcesarPendientesAsync(CancellationToken ct = default)
    {
        if (!_sesion.TienePermiso(RolUsuario.ADMIN))
            throw new UnauthorizedAccessException("Solo ADMIN puede procesar eventos Outbox.");

        int procesados = 0;
        int fallidos = 0;
        var pendientes = await _repositorio.ObtenerEventosOutboxPendientesAsync(ct);
        foreach (var item in pendientes)
        {
            try
            {
                var evento = CrearEvento(item.TipoEvento, item.PayloadJson);
                await _publicador.PublicarEventoAsync(evento, ct);
                await _repositorio.MarcarOutboxProcesadoAsync(item.Id, ct);
                procesados++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await _repositorio.RegistrarFalloOutboxAsync(item.Id, ex.Message, ct);
                fallidos++;
            }
        }

        return new ResultadoProcesamientoOutbox(procesados, fallidos);
    }

    private static EventoSistemaLogiMarket CrearEvento(string tipoEvento, string payload)
    {
        using var documento = JsonDocument.Parse(payload);
        var raiz = documento.RootElement;
        string codigo = LeerTexto(raiz, "Codigo") ?? "";
        string cliente = LeerTexto(raiz, "NombreCliente") ?? "";
        string detalle = tipoEvento switch
        {
            "PedidoConfirmado" => $"Pedido {codigo} confirmado. Referencia: {LeerTexto(raiz, "ReferenciaPasarela") ?? ""}",
            "PedidoDespachado" => $"Pedido {codigo} despachado. Guía: {LeerTexto(raiz, "Guia") ?? ""}",
            "PedidoEntregado" => $"Pedido {codigo} entregado",
            "DevolucionProcesada" => $"Devolución {LeerTexto(raiz, "Motivo") ?? ""} del pedido {codigo} aprobada",
            _ => throw new InvalidOperationException($"Tipo de evento Outbox no soportado: {tipoEvento}")
        };

        return new EventoSistemaLogiMarket(tipoEvento, codigo, cliente, LeerDecimal(raiz, "Total"), detalle, DateTime.UtcNow);
    }

    private static string? LeerTexto(JsonElement raiz, string nombre) =>
        raiz.TryGetProperty(nombre, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;

    private static decimal LeerDecimal(JsonElement raiz, string nombre) =>
        raiz.TryGetProperty(nombre, out var valor) && valor.TryGetDecimal(out var numero)
            ? numero
            : decimal.Zero;
}
