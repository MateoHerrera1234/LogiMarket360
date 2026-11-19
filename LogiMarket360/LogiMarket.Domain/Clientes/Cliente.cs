using System;
using LogiMarket.Domain.Enums;

namespace LogiMarket.Domain.Clientes;

// Entidad de dominio que representa a un cliente con su categoria comercial
public sealed class Cliente
{
    public Guid Id { get; private set; }
    public string Codigo { get; private set; }
    public string Nombre { get; private set; }
    public string Contacto { get; private set; }
    public CategoriaCliente Categoria { get; private set; }
    public bool Activo { get; private set; }

    public Cliente(Guid id, string codigo, string nombre, string contacto, CategoriaCliente categoria, bool activo = true)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            throw new ArgumentException("El codigo del cliente es obligatorio", nameof(codigo));

        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre del cliente es obligatorio", nameof(nombre));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        Codigo = codigo.Trim().ToUpperInvariant();
        Nombre = nombre.Trim();
        Contacto = contacto?.Trim() ?? string.Empty;
        Categoria = categoria;
        Activo = activo;
    }

    public void ActualizarDatos(string nombre, string contacto, CategoriaCliente categoria, bool activo)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre del cliente es obligatorio", nameof(nombre));

        Nombre = nombre.Trim();
        Contacto = contacto?.Trim() ?? string.Empty;
        Categoria = categoria;
        Activo = activo;
    }
}
