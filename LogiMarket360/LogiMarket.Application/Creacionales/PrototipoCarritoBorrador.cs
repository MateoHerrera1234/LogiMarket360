using System;
using System.Collections.Generic;
using System.Linq;
using LogiMarket.Domain.Enums;

namespace LogiMarket.Application.Creacionales;

// Item mutable de una plantilla de carrito borrador
public sealed class ItemBorradorPrototipo
{
    public string Sku { get; set; }
    public string Nombre { get; set; }
    public int Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }

    public ItemBorradorPrototipo(string sku, string nombre, int cantidad, decimal precioUnitario)
    {
        Sku = sku;
        Nombre = nombre;
        Cantidad = cantidad;
        PrecioUnitario = precioUnitario;
    }

    public ItemBorradorPrototipo ClonarProfundo() =>
        new ItemBorradorPrototipo(Sku, Nombre, Cantidad, PrecioUnitario);
}

// Implementacion del patron Prototype para clonar borradores de carrito sin copiar ID ni pago
public sealed class PlantillaCarritoBorrador
{
    public string NombrePlantilla { get; set; }
    public Guid? ClienteId { get; set; }
    public TipoEntrega TipoEntrega { get; set; }
    public List<ItemBorradorPrototipo> Items { get; private set; }

    public PlantillaCarritoBorrador(string nombrePlantilla, Guid? clienteId, TipoEntrega tipoEntrega, IEnumerable<ItemBorradorPrototipo> items)
    {
        NombrePlantilla = nombrePlantilla;
        ClienteId = clienteId;
        TipoEntrega = tipoEntrega;
        Items = items?.Select(i => i.ClonarProfundo()).ToList() ?? new List<ItemBorradorPrototipo>();
    }

    public PlantillaCarritoBorrador Clonar()
    {
        var itemsClonados = Items.Select(item => item.ClonarProfundo()).ToList();
        return new PlantillaCarritoBorrador(
            $"{NombrePlantilla}_COPIA",
            ClienteId,
            TipoEntrega,
            itemsClonados);
    }
}
