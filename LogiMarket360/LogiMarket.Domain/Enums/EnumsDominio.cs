namespace LogiMarket.Domain.Enums;

// Define los estados validos del ciclo de vida de un pedido
public enum EstadoPedido
{
    BORRADOR = 0,
    VALIDADO = 1,
    PAGADO = 2,
    DESPACHADO = 3,
    ENTREGADO = 4,
    CANCELADO = 5
}

// Define las categorias comerciales de un cliente para promociones
public enum CategoriaCliente
{
    NORMAL = 0,
    VIP = 1,
    MAYORISTA = 2
}

// Define las modalidades de entrega disponibles en el sistema
public enum TipoEntrega
{
    ESTANDAR = 0,
    EXPRES = 1
}

// Define los motivos admitidos para una devolucion total
public enum MotivoDevolucion
{
    DEFECTO = 0,
    ARREPENTIMIENTO = 1
}

// Define los roles operativos para control de acceso en aplicacion
public enum RolUsuario
{
    ADMIN = 0,
    VENTAS = 1,
    BODEGA = 2,
    CONSULTA = 3
}
