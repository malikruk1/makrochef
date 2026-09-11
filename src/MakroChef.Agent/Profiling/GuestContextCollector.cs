using System.Text.Json;
using MakroChef.Domain.Profile;
using MakroChef.Mcp;

namespace MakroChef.Agent.Profiling;

/// <summary>TASKS.md 4.1. 🔒 needs a live token (BLOCKERS.md B-2) — code is complete and unit
/// tested against fixtures, but the real field names in get_my_profile/get_my_family/etc. are
/// unknown until then (tools/list only gives input schemas). Same schema-agnostic scanning
/// approach as the 3.4 coverage probe.</summary>
public class GuestContextCollector(IMakroChefMcpClient mcpClient)
{
    public async Task<GuestProfile> CollectAsync(CancellationToken cancellationToken = default)
    {
        var emptyArgs = new Dictionary<string, object?>();

        var profileJson = await mcpClient.CallToolAsync("get_my_profile", emptyArgs, cancellationToken);
        var familyJson = await mcpClient.CallToolAsync("get_my_family", emptyArgs, cancellationToken);
        var restrictionsJson = await mcpClient.CallToolAsync("get_my_food_restrictions", emptyArgs, cancellationToken);
        var addressesJson = await mcpClient.CallToolAsync("get_my_delivery_addresses", emptyArgs, cancellationToken);
        var loyaltyJson = await mcpClient.CallToolAsync("get_loyalty_info", emptyArgs, cancellationToken);

        return new GuestProfile(
            AgeYears: ExtractAge(profileJson),
            Family: ExtractFamily(familyJson),
            Restrictions: ExtractStringArray(restrictionsJson, "restrictions"),
            HasSavedAddress: ExtractHasAddresses(addressesJson),
            LoyaltyBonusBalance: ExtractLoyaltyBalance(loyaltyJson));
    }

    private static int? ExtractAge(string json)
    {
        var root = TryParseObject(json);
        if (root is null)
        {
            return null;
        }

        foreach (var key in new[] { "birthDate", "dateOfBirth", "birthdate" })
        {
            if (root.Value.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(value.GetString(), out var birthDate))
            {
                var today = DateTimeOffset.UtcNow;
                var age = today.Year - birthDate.Year;
                if (birthDate.AddYears(age) > today)
                {
                    age--;
                }

                return age;
            }
        }

        return null;
    }

    private static List<FamilyMember> ExtractFamily(string json)
    {
        var members = new List<FamilyMember>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var arrayElement = root.ValueKind == JsonValueKind.Array
            ? root
            : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("members", out var m) && m.ValueKind == JsonValueKind.Array
                ? m
                : (JsonElement?)null;

        if (arrayElement is null)
        {
            return members;
        }

        foreach (var member in arrayElement.Value.EnumerateArray())
        {
            int? age = null;
            foreach (var key in new[] { "age", "ageYears" })
            {
                if (member.TryGetProperty(key, out var ageValue) && ageValue.ValueKind == JsonValueKind.Number)
                {
                    age = ageValue.GetInt32();
                    break;
                }
            }

            members.Add(new FamilyMember(age));
        }

        return members;
    }

    private static List<string> ExtractStringArray(string json, string wrapperPropertyName)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var arrayElement = root.ValueKind == JsonValueKind.Array
            ? root
            : root.ValueKind == JsonValueKind.Object && root.TryGetProperty(wrapperPropertyName, out var w) && w.ValueKind == JsonValueKind.Array
                ? w
                : (JsonElement?)null;

        if (arrayElement is null)
        {
            return [];
        }

        return arrayElement.Value.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToList();
    }

    private static bool ExtractHasAddresses(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return root.ValueKind == JsonValueKind.Array
            ? root.GetArrayLength() > 0
            : root.ValueKind == JsonValueKind.Object
              && root.TryGetProperty("addresses", out var addresses)
              && addresses.ValueKind == JsonValueKind.Array
              && addresses.GetArrayLength() > 0;
    }

    private static decimal ExtractLoyaltyBalance(string json)
    {
        var root = TryParseObject(json);
        if (root is null)
        {
            return 0;
        }

        foreach (var key in new[] { "bonusBalance", "balance", "loyaltyBalance" })
        {
            if (root.Value.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number)
            {
                return value.GetDecimal();
            }
        }

        return 0;
    }

    private static JsonElement? TryParseObject(string json)
    {
        var root = JsonDocument.Parse(json).RootElement;
        return root.ValueKind == JsonValueKind.Object ? root : null;
    }
}
