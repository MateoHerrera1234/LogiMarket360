namespace LogiMarket.Domain.Catalogo;

// Contrato del patron Visitor para operaciones de reporte y calculo sobre el catalogo
public interface IVisitorCatalogo
{
    void VisitarProducto(ProductoSimple producto, int cantidad);
    void VisitarKit(KitCompuesto kit, int cantidad);
}
