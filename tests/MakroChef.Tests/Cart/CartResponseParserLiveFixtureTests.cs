using MakroChef.Agent.Cart;
using Xunit;

namespace MakroChef.Tests.Cart;

/// <summary>Regression test pinned to an actual silpo_get_shopping_cart_by_id response
/// captured 2026-09-13 against a live account (BLOCKERS.md B-2 finally lifted). Everything is
/// nested under "cart" -> shipments[].products[] / calculation.validations[] /
/// calculation.totalAfterDiscounts, not the flat shape originally guessed from tools/list input
/// schemas alone. Personal data (address, coordinates) stripped, product IDs kept.</summary>
public class CartResponseParserLiveFixtureTests
{
    private const string LiveCartJson = """
        {"success":true,"cart":{"id":"be85fab0-1ad0-4bbc-a0b0-e6566a63b48f","deliveryType":"SelfPickup","timeslot":{"start":"2026-09-14T06:00:00+00:00","end":"2026-09-14T06:30:00+00:00"},"shipments":[{"id":"a4b4e737-3a92-4322-b354-26bd2ab50130","companyId":"1ec88c5d-a050-669c-8467-570a157f3e31","branchId":"1ef86dfb-5d4d-6a20-9377-494ed979998f","products":[{"productId":"1edafa8e-3cdd-6ae0-a73c-df42cf4d0987","companyId":"1ec88c5d-a050-669c-8467-570a157f3e31","branchId":"00000000-0000-0000-0000-000000000000","slug":"pitsa-amerykana-747288","name":"Піца Американа","quantity":1,"price":109,"stock":100,"weighted":false},{"productId":"1f0240fa-2531-68ae-b707-8501ffe8b5bf","companyId":"1ec88c5d-a050-669c-8467-570a157f3e31","branchId":"00000000-0000-0000-0000-000000000000","slug":"pitsa-shkilna-989139","name":"Піца Шкільна","quantity":1,"price":99,"stock":100,"weighted":false}]}],"promoCode":null,"calculation":{"total":217,"totalAfterDiscounts":217,"certificatesTotal":0,"subTotal":337,"subDiscount":120,"productsTotal":208,"validations":[{"level":"error","type":"product","message":"product.offer.status.not_available","context":{"productId":"1edafa8e-3cdd-6ae0-a73c-df42cf4d0987","markdownGroup":"default"}},{"level":"error","type":"product","message":"product.offer.status.not_available","context":{"productId":"1f0240fa-2531-68ae-b707-8501ffe8b5bf","markdownGroup":"default"}},{"level":"error","type":"product","message":"product.offer.stock.max","context":{"productId":"1edafa8e-3cdd-6ae0-a73c-df42cf4d0987","markdownGroup":"default","stock":0}},{"level":"info","type":"order","message":"order.payment_types.disabled","context":{"reason":"not_available_for_total","paymentTypes":["BNPL"],"total":217,"minTotal":1000}}]}},"loyalty":{"bonusAvailable":8.09,"bonusTotal":8.09,"bonusRequested":null,"isEnabled":true}}
        """;

    [Fact]
    public void Parse_LiveCartFixture_ExtractsLinesFromNestedShipments()
    {
        var cart = CartResponseParser.Parse(LiveCartJson);

        Assert.Equal(2, cart.Lines.Count);
        Assert.Contains(cart.Lines, l => l.ProductId == "1edafa8e-3cdd-6ae0-a73c-df42cf4d0987" && l.Quantity == 1);
        Assert.Contains(cart.Lines, l => l.ProductId == "1f0240fa-2531-68ae-b707-8501ffe8b5bf" && l.Quantity == 1);
    }

    [Fact]
    public void Parse_LiveCartFixture_ExtractsTotalAfterDiscountsInKopecks()
    {
        var cart = CartResponseParser.Parse(LiveCartJson);

        Assert.Equal(21_700, cart.TotalKopecks); // 217 UAH -> 21700 kopecks
    }

    [Fact]
    public void Parse_LiveCartFixture_FindsBothNotAvailableProductsAsOutOfStock()
    {
        var cart = CartResponseParser.Parse(LiveCartJson);

        var outOfStockIds = cart.Validations.Where(v => v.IsOutOfStock).Select(v => v.ProductId).ToHashSet();

        Assert.Contains("1edafa8e-3cdd-6ae0-a73c-df42cf4d0987", outOfStockIds);
        Assert.Contains("1f0240fa-2531-68ae-b707-8501ffe8b5bf", outOfStockIds);
    }

    [Fact]
    public void Parse_LiveCartFixture_InfoLevelPaymentValidation_IsNotFlaggedAsOutOfStock()
    {
        var cart = CartResponseParser.Parse(LiveCartJson);

        var paymentValidation = cart.Validations.Single(v => v.Reason == "order.payment_types.disabled");
        Assert.False(paymentValidation.IsOutOfStock);
    }
}
