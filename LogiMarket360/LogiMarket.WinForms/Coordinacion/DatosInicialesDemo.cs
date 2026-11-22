using System;
using System.Threading;
using System.Threading.Tasks;
using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Enums;

namespace LogiMarket.WinForms.Coordinacion;

// Carga datos iniciales de demostracion solo si la base de datos esta vacia
public static class DatosInicialesDemo
{
    public static async Task CargarSiVacioAsync(CoordinadorLogiMarketWinForms coord, CancellationToken ct = default)
    {
        var existentes = await coord.Catalogo.ListarTodosAsync(ct);
        if (existentes.Count > 0)
            return;

        var p1 = new ProductoSimple("SKU-101", "Caja Termica Industrial", 45.00m, 2.50m, 40);
        var p2 = new ProductoSimple("SKU-102", "Sensor Temperatura IoT", 28.50m, 0.35m, 60);
        var p3 = new ProductoSimple("SKU-103", "Etiqueta RFID Rollo x100", 15.00m, 0.40m, 80);

        await coord.Catalogo.GuardarProductoAsync(p1, ct);
        await coord.Catalogo.GuardarProductoAsync(p2, ct);
        await coord.Catalogo.GuardarProductoAsync(p3, ct);

        var kit = new KitCompuesto("KIT-01", "Kit Cadena de Frio Basico", 82.00m);
        kit.AgregarComponente(p1, 1);
        kit.AgregarComponente(p2, 1);
        kit.AgregarComponente(p3, 1);
        await coord.Catalogo.GuardarKitAsync(kit, ct);

        await coord.Clientes.GuardarAsync(new Cliente(Guid.NewGuid(), "CLI-001", "Distribuidora Andina S.A.", "compras@andina.ec", CategoriaCliente.MAYORISTA, true), ct);
        await coord.Clientes.GuardarAsync(new Cliente(Guid.NewGuid(), "CLI-002", "Supermercados El Sol", "logistica@elsol.ec", CategoriaCliente.VIP, true), ct);
        await coord.Clientes.GuardarAsync(new Cliente(Guid.NewGuid(), "CLI-003", "Comercial Tungurahua", "ventas@tungurahua.ec", CategoriaCliente.NORMAL, true), ct);
    }
}
