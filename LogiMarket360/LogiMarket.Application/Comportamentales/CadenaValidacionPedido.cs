using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LogiMarket.Application.Puertos;
using LogiMarket.Domain.Enums;
using LogiMarket.Domain.Pedidos;

namespace LogiMarket.Application.Comportamentales;

// Contexto evaluado por la cadena secuencial de validaciones antes de confirmar un pedido
public sealed record ContextoValidacionPedido(Pedido Pedido, string ClaveIdempotencia);

// Resultado de la validacion encadenada
public sealed record ResultadoValidacionCadena(bool EsValido, string? MotivoRechazo, string? EslabonFallo);

// Contrato del eslabon en Chain of Responsibility
public interface IManejadorValidacionPedido
{
    IManejadorValidacionPedido EstablecerSiguiente(IManejadorValidacionPedido siguiente);
    Task<ResultadoValidacionCadena> ValidarAsync(ContextoValidacionPedido contexto, CancellationToken ct = default);
}

// Clase base para evitar repetir el encadenamiento entre eslabones
public abstract class ManejadorValidacionBase : IManejadorValidacionPedido
{
    private IManejadorValidacionPedido? _siguiente;

    public IManejadorValidacionPedido EstablecerSiguiente(IManejadorValidacionPedido siguiente)
    {
        _siguiente = siguiente;
        return siguiente;
    }

    public async Task<ResultadoValidacionCadena> ValidarAsync(ContextoValidacionPedido contexto, CancellationToken ct = default)
    {
        var propio = await EjecutarValidacionAsync(contexto, ct);
        if (!propio.EsValido)
            return propio;

        if (_siguiente is null)
            return new ResultadoValidacionCadena(true, null, null);

        return await _siguiente.ValidarAsync(contexto, ct);
    }

    protected abstract Task<ResultadoValidacionCadena> EjecutarValidacionAsync(ContextoValidacionPedido contexto, CancellationToken ct);
}

// Eslabon 1: valida que el cliente exista y este activo
public sealed class ValidadorClienteActivoHandler : ManejadorValidacionBase
{
    private readonly IRepositorioClientes _repositorioClientes;

    public ValidadorClienteActivoHandler(IRepositorioClientes repositorioClientes)
    {
        _repositorioClientes = repositorioClientes ?? throw new ArgumentNullException(nameof(repositorioClientes));
    }

    protected override async Task<ResultadoValidacionCadena> EjecutarValidacionAsync(ContextoValidacionPedido contexto, CancellationToken ct)
    {
        var cliente = await _repositorioClientes.ObtenerPorIdAsync(contexto.Pedido.ClienteId, ct);
        if (cliente is null)
            return new ResultadoValidacionCadena(false, "El cliente asociado al pedido no existe", nameof(ValidadorClienteActivoHandler));

        if (!cliente.Activo)
            return new ResultadoValidacionCadena(false, $"El cliente {cliente.Codigo} se encuentra inactivo", nameof(ValidadorClienteActivoHandler));

        return new ResultadoValidacionCadena(true, null, null);
    }
}

// Eslabon 2: valida que existan lineas y cantidades positivas
public sealed class ValidadorLineasCantidadesHandler : ManejadorValidacionBase
{
    protected override Task<ResultadoValidacionCadena> EjecutarValidacionAsync(ContextoValidacionPedido contexto, CancellationToken ct)
    {
        if (contexto.Pedido.Lineas.Count == 0)
            return Task.FromResult(new ResultadoValidacionCadena(false, "El pedido no contiene lineas", nameof(ValidadorLineasCantidadesHandler)));

        foreach (var linea in contexto.Pedido.Lineas)
        {
            if (linea.Cantidad <= 0)
                return Task.FromResult(new ResultadoValidacionCadena(false, $"La linea {linea.Sku} tiene cantidad invalida", nameof(ValidadorLineasCantidadesHandler)));
        }

        return Task.FromResult(new ResultadoValidacionCadena(true, null, null));
    }
}

// Eslabon 3: valida disponibilidad de inventario para cada SKU fisico expandido
public sealed class ValidadorStockDisponibleHandler : ManejadorValidacionBase
{
    private readonly IRepositorioCatalogo _repositorioCatalogo;

    public ValidadorStockDisponibleHandler(IRepositorioCatalogo repositorioCatalogo)
    {
        _repositorioCatalogo = repositorioCatalogo ?? throw new ArgumentNullException(nameof(repositorioCatalogo));
    }

    protected override async Task<ResultadoValidacionCadena> EjecutarValidacionAsync(ContextoValidacionPedido contexto, CancellationToken ct)
    {
        foreach (var (sku, requerida) in contexto.Pedido.ObtenerRequerimientosStockPorSku())
        {
            var elemento = await _repositorioCatalogo.ObtenerPorSkuAsync(sku, ct);
            if (elemento is null)
                return new ResultadoValidacionCadena(false, $"SKU fisico {sku} no encontrado en catalogo", nameof(ValidadorStockDisponibleHandler));

            if (elemento.StockDisponible < requerida)
                return new ResultadoValidacionCadena(
                    false,
                    $"Stock insuficiente para {sku}: hay {elemento.StockDisponible} y se requieren {requerida}",
                    nameof(ValidadorStockDisponibleHandler));
        }

        return new ResultadoValidacionCadena(true, null, null);
    }
}

// Eslabon 4: valida permiso de usuario en sesion para confirmar pedidos
public sealed class ValidadorAutorizacionConfirmacionHandler : ManejadorValidacionBase
{
    private readonly IContextoSesion _sesion;

    public ValidadorAutorizacionConfirmacionHandler(IContextoSesion sesion)
    {
        _sesion = sesion ?? throw new ArgumentNullException(nameof(sesion));
    }

    protected override Task<ResultadoValidacionCadena> EjecutarValidacionAsync(ContextoValidacionPedido contexto, CancellationToken ct)
    {
        if (!_sesion.TienePermiso(RolUsuario.VENTAS, RolUsuario.ADMIN))
        {
            string rol = _sesion.UsuarioActual?.Rol.ToString() ?? "SIN_SESION";
            return Task.FromResult(new ResultadoValidacionCadena(
                false,
                $"El rol {rol} no tiene autorizacion para confirmar pedidos",
                nameof(ValidadorAutorizacionConfirmacionHandler)));
        }

        return Task.FromResult(new ResultadoValidacionCadena(true, null, null));
    }
}

// Fabrica que arma la cadena en el orden requerido por la guia
public static class EnsambladorCadenaValidacion
{
    public static IManejadorValidacionPedido CrearCadenaConfirmacion(
        IRepositorioClientes clientes,
        IRepositorioCatalogo catalogo,
        IContextoSesion sesion)
    {
        var primero = new ValidadorClienteActivoHandler(clientes);
        primero
            .EstablecerSiguiente(new ValidadorLineasCantidadesHandler())
            .EstablecerSiguiente(new ValidadorStockDisponibleHandler(catalogo))
            .EstablecerSiguiente(new ValidadorAutorizacionConfirmacionHandler(sesion));

        return primero;
    }
}
