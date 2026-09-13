namespace MakroChef.Domain.Catalog;

/// <summary>Parsed get_product_details, price already net of any promotion/coupon discount
/// found for this SKU (TASKS.md 5.1: "Ціни подавати після знижок").</summary>
public record ProductDetails(
    string ProductId,
    string Category,
    long PriceKopecks,
    bool OnPromotion,
    string? Barcode,
    decimal WeightGrams,
    // Confirmed live (2026-09-14): real get_product_details carries "name" at the product root
    // (e.g. "Снеки Чікенззз Peri-Peri з м'ясом птиці в/к") - null when absent/unparseable.
    string? Name = null);
