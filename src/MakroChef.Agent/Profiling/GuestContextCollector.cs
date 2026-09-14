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

        var profileJson = await mcpClient.CallToolAsync("silpo_get_my_profile", emptyArgs, cancellationToken);
        var familyJson = await mcpClient.CallToolAsync("silpo_get_my_family", emptyArgs, cancellationToken);
        var restrictionsJson = await mcpClient.CallToolAsync("silpo_get_my_food_restrictions", emptyArgs, cancellationToken);
        var addressesJson = await mcpClient.CallToolAsync("silpo_get_my_delivery_addresses", emptyArgs, cancellationToken);
        var loyaltyJson = await mcpClient.CallToolAsync("silpo_get_loyalty_info", emptyArgs, cancellationToken);

        return new GuestProfile(
            AgeYears: ExtractAge(profileJson),
            Family: ExtractFamily(familyJson),
            Restrictions: ExtractRestrictions(restrictionsJson),
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

        // Real shape (confirmed live 2026-09-14): wrapped in "profile", key is "birthday"
        // ("2001-07-15", date-only) - not "birthDate" as originally guessed.
        var profile = root.Value.TryGetProperty("profile", out var p) && p.ValueKind == JsonValueKind.Object ? p : root.Value;

        foreach (var key in new[] { "birthday", "birthDate", "dateOfBirth", "birthdate" })
        {
            if (profile.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
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

    /// <summary>Confirmed live (2026-09-14): silpo_get_my_family wraps two SEPARATE arrays -
    /// "members" (adult household members, each carrying no age at all, and including the guest
    /// themselves flagged "itsMe":true) and "children" (kept separate precisely because the API
    /// doesn't expect you to infer child-vs-adult from an age field). Previously assumed a single
    /// flat "members" array with an "age" per entry - that shape doesn't exist; every real member
    /// silently produced FamilyMember(null), and the guest was double-counted into household size
    /// alongside the hardcoded "+1" in /api/profile.</summary>
    private static List<FamilyMember> ExtractFamily(string json)
    {
        var members = new List<FamilyMember>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            return members;
        }

        if (root.TryGetProperty("members", out var memberArray) && memberArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var member in memberArray.EnumerateArray())
            {
                if (member.TryGetProperty("itsMe", out var itsMe) && itsMe.ValueKind == JsonValueKind.True)
                {
                    continue; // the guest themselves - already counted as the "+1" household head
                }

                members.Add(new FamilyMember(ExtractMemberAge(member)));
            }
        }

        if (root.TryGetProperty("children", out var childrenArray) && childrenArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in childrenArray.EnumerateArray())
            {
                members.Add(new FamilyMember(ExtractMemberAge(child), ForcedIsChild: true));
            }
        }

        return members;
    }

    private static int? ExtractMemberAge(JsonElement member)
    {
        foreach (var key in new[] { "age", "ageYears" })
        {
            if (member.TryGetProperty(key, out var ageValue) && ageValue.ValueKind == JsonValueKind.Number)
            {
                return ageValue.GetInt32();
            }
        }

        return null;
    }

    /// <summary>Confirmed live (2026-09-14): silpo_get_my_food_restrictions' "restrictions" array
    /// holds OBJECTS (<c>{"slug":"...", "name": "..." | null}</c>), not plain strings as
    /// originally guessed - every real restriction was silently dropped before (the string-only
    /// filter matched nothing). Prefers "name" when set, falls back to "slug".</summary>
    private static List<string> ExtractRestrictions(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var arrayElement = root.ValueKind == JsonValueKind.Array
            ? root
            : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("restrictions", out var w) && w.ValueKind == JsonValueKind.Array
                ? w
                : (JsonElement?)null;

        if (arrayElement is null)
        {
            return [];
        }

        var restrictions = new List<string>();
        foreach (var element in arrayElement.Value.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                restrictions.Add(element.GetString()!);
                continue;
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (element.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            {
                restrictions.Add(name.GetString()!);
            }
            else if (element.TryGetProperty("slug", out var slug) && slug.ValueKind == JsonValueKind.String)
            {
                restrictions.Add(slug.GetString()!);
            }
        }

        return restrictions;
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

        // Real shape (confirmed live 2026-09-14): wrapped in "loyalty", the actual balance is
        // loyalty.balance.total (a nested object, not a bare number) - "balance" alone is an
        // object here, not the amount itself, unlike originally guessed.
        var loyalty = root.Value.TryGetProperty("loyalty", out var l) && l.ValueKind == JsonValueKind.Object ? l : root.Value;

        if (loyalty.TryGetProperty("balance", out var balanceObj) && balanceObj.ValueKind == JsonValueKind.Object
            && balanceObj.TryGetProperty("total", out var total) && total.ValueKind == JsonValueKind.Number)
        {
            return total.GetDecimal();
        }

        foreach (var key in new[] { "bonusBalance", "balance", "loyaltyBalance" })
        {
            if (loyalty.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number)
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
