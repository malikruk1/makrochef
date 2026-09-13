using MakroChef.Nutrition;
using Xunit;

namespace MakroChef.Tests.Nutrition;

/// <summary>Regression test pinned to an actual silpo_get_product_details response captured
/// 2026-09-14 against a live account (BLOCKERS.md B-2). Nutrients live under Ukrainian
/// food-label keys in product.attributes, not the English keys originally guessed from
/// tools/list input schemas alone.</summary>
public class ExactMcpNutritionResolverLiveFixtureTests
{
    private const string LivePizzaJson = """
        {"success":true,"product":{"id":"1edafa8e-3cdd-6ae0-a73c-df42cf4d0987","name":"Піца Американа","slug":"pitsa-amerykana-747288","price":109,"displayPrice":109,"oldPrice":189,"stock":0,"available":false,"weighted":false,"step":1,"displayRatio":"500г","companyId":"1ec88c5d-a050-669c-8467-570a157f3e31","branchId":"00000000-0000-0000-0000-000000000000","externalProductId":747288,"ratio":"шт","attributes":{"Склад":"тісто для піци...","Містить алергени":"ПШЕНИЦЮ, МОЛОКО","Країна":"Україна","Торгова марка":"Без ТМ","Продавець":"ТОВ «СІЛЬПО-ФУД»","Енергетична цінність (кКал/кДЖ)":"241/1013","Білки (г)":10,"Жири (г)":8.2,"Вуглеводи (г)":31.9}}}
        """;

    [Fact]
    public void ParseNutrients_LivePizzaFixture_ExtractsProteinFatCarbsFromUkrainianKeys()
    {
        var (protein, fat, carbs, sugar, kcal) = ExactMcpNutritionResolver.ParseNutrients(LivePizzaJson);

        Assert.Equal(10m, protein);
        Assert.Equal(8.2m, fat);
        Assert.Equal(31.9m, carbs);
        Assert.Null(sugar); // not present on this particular product - honest null, not a guess
        Assert.Equal(241m, kcal); // "241/1013" (kcal/kJ) -> 241
    }

    [Fact]
    public void ParseNutrients_MissingAttributes_ReturnsAllNull()
    {
        var (protein, fat, carbs, sugar, kcal) = ExactMcpNutritionResolver.ParseNutrients("""{"success":true,"product":{"id":"x"}}""");

        Assert.Null(protein);
        Assert.Null(fat);
        Assert.Null(carbs);
        Assert.Null(sugar);
        Assert.Null(kcal);
    }
}
