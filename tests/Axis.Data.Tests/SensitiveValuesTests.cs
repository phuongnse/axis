using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Records;
using Axis.Tenancy.Keys;

namespace Axis.Data.Tests;

public sealed class SensitiveValuesTests
{
    private const string Tenant = "acme";

    private static readonly Guid _entityId = Guid.Parse("6f1a0c3e-5b7d-4e21-9a40-0c1d2e3f4a51");

    private static readonly Guid _recordId = Guid.Parse("a2b3c4d5-e6f7-4812-93a4-b5c6d7e8f901");

    private static readonly SensitiveValueScope _scope = new(Tenant, _entityId, _recordId);

    private static readonly Guid _targetId = Guid.Parse("3d9e8f70-1a2b-4c3d-8e4f-5a6b7c8d9e0f");

    private static readonly FieldModel _text = Models.Field("note", FieldType.Text);

    public static TheoryData<FieldType, object, string> Values => new()
    {
        { FieldType.Text, "Ngô Thị Hạnh", "\"Ngô Thị Hạnh\"" },
        { FieldType.Enum, "open", "\"open\"" },
        { FieldType.Integer, 42L, "42" },
        { FieldType.Decimal, "1250.5", "1250.50" },
        { FieldType.Money, "-3", "-3.00" },
        { FieldType.Boolean, true, "true" },
        { FieldType.Date, new DateOnly(2026, 11, 2), "\"2026-11-02\"" },
        { FieldType.DateTime, new DateTimeOffset(2026, 11, 2, 8, 30, 15, TimeSpan.Zero), "\"2026-11-02T08:30:15.000000Z\"" },
        { FieldType.Reference, _targetId, "\"3d9e8f70-1a2b-4c3d-8e4f-5a6b7c8d9e0f\"" },
        { FieldType.File, _targetId, "\"3d9e8f70-1a2b-4c3d-8e4f-5a6b7c8d9e0f\"" },
    };

    [Theory]
    [MemberData(nameof(Values))]
    public async Task A_value_of_each_type_round_trips_as_the_record_api_shows_it(FieldType type, object value, string json)
    {
        var field = FieldOf(type);
        var cipher = new SensitiveValues(new TenantKeys(new FakeSource(Key(TenantKeyKind.Data, 1))));

        var stored = await cipher.EncryptAsync(_scope, new RecordValue(field, value), CancellationToken.None);
        var plain = await cipher.DecryptAsync(_scope, field, stored!, CancellationToken.None);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), plain), plain.ToJsonString());
        Assert.Equal(JsonNode.Parse(json)!.ToJsonString(), plain.ToJsonString());
    }

    [Fact]
    public async Task A_null_value_is_not_encrypted()
    {
        var cipher = new SensitiveValues(new TenantKeys(new FakeSource(Key(TenantKeyKind.Data, 1))));

        Assert.Null(await cipher.EncryptAsync(_scope, new RecordValue(_text, null), CancellationToken.None));
    }

    [Fact]
    public async Task A_stored_value_is_the_key_number_a_nonce_the_ciphertext_and_a_tag()
    {
        var cipher = CipherWith(Key(TenantKeyKind.Data, 1), Key(TenantKeyKind.Data, 2), Key(TenantKeyKind.Lookup, 3));
        const string text = "Ngô Thị Hạnh";

        var stored = await cipher.EncryptAsync(_scope, new RecordValue(_text, text), CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal(4 + 12 + Encoding.UTF8.GetByteCount(text) + 16, stored.Length);
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(stored));
        Assert.Equal([0, 0, 0, 2], stored[..4]);
        var other = await cipher.EncryptAsync(_scope, new RecordValue(_text, text), CancellationToken.None);
        Assert.NotEqual(stored[4..16], other![4..16]);
    }

    [Fact]
    public async Task A_value_does_not_decrypt_for_another_tenant_entity_record_or_field()
    {
        var cipher = new SensitiveValues(new TenantKeys(new FakeSource(Key(TenantKeyKind.Data, 1))));
        var stored = (await cipher.EncryptAsync(_scope, new RecordValue(_text, "secret"), CancellationToken.None))!;

        await AssertFailsAsync(cipher, _scope with { TenantId = "globex" }, _text, stored);
        await AssertFailsAsync(cipher, _scope with { EntityId = Guid.NewGuid() }, _text, stored);
        await AssertFailsAsync(cipher, _scope with { RecordId = Guid.NewGuid() }, _text, stored);
        await AssertFailsAsync(cipher, _scope, Models.Field("other", FieldType.Text), stored);
    }

    [Fact]
    public async Task A_field_name_is_compared_in_lower_case()
    {
        var cipher = new SensitiveValues(new TenantKeys(new FakeSource(Key(TenantKeyKind.Data, 1))));
        var stored = (await cipher.EncryptAsync(_scope, new RecordValue(Models.Field("Note", FieldType.Text), "secret"), CancellationToken.None))!;

        var plain = await cipher.DecryptAsync(_scope, Models.Field("NOTE", FieldType.Text), stored, CancellationToken.None);

        Assert.Equal("secret", plain.GetValue<string>());
    }

    [Fact]
    public async Task A_value_with_any_byte_changed_does_not_decrypt()
    {
        var cipher = new SensitiveValues(new TenantKeys(new FakeSource(Key(TenantKeyKind.Data, 1))));
        var stored = (await cipher.EncryptAsync(_scope, new RecordValue(_text, "secret"), CancellationToken.None))!;

        for (var index = 0; index < stored.Length; index++)
        {
            var changed = (byte[])stored.Clone();
            changed[index] ^= 0x01;
            await AssertFailsAsync(cipher, _scope, _text, changed);
        }
    }

    [Fact]
    public async Task A_value_that_is_too_short_does_not_decrypt()
    {
        var cipher = new SensitiveValues(new TenantKeys(new FakeSource(Key(TenantKeyKind.Data, 1))));

        await AssertFailsAsync(cipher, _scope, _text, new byte[4 + 12 + 16 - 1]);
        await AssertFailsAsync(cipher, _scope, _text, []);
    }

    [Fact]
    public async Task A_tenant_without_a_data_key_cannot_encrypt()
    {
        var none = new SensitiveValues(new TenantKeys(new FakeSource()));
        var lookupOnly = CipherWith(Key(TenantKeyKind.Lookup, 1));

        var exception = await Assert.ThrowsAsync<TenantDataKeyMissingException>(
            () => none.EncryptAsync(_scope, new RecordValue(_text, "secret"), CancellationToken.None));
        await Assert.ThrowsAsync<TenantDataKeyMissingException>(
            () => lookupOnly.EncryptAsync(_scope, new RecordValue(_text, "secret"), CancellationToken.None));

        Assert.Equal(Tenant, exception.TenantId);
        Assert.Contains(Tenant, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_value_under_a_key_added_later_decrypts_after_one_more_read()
    {
        var source = new FakeSource(Key(TenantKeyKind.Data, 1), Key(TenantKeyKind.Data, 2));
        var reader = new SensitiveValues(new TenantKeys(source));
        var first = (await reader.EncryptAsync(_scope, new RecordValue(_text, "one"), CancellationToken.None))!;
        await reader.DecryptAsync(_scope, _text, first, CancellationToken.None);
        Assert.Equal(1, source.Reads);

        source.Add(Key(TenantKeyKind.Data, 3));
        var writer = new SensitiveValues(new TenantKeys(source));
        var later = (await writer.EncryptAsync(_scope, new RecordValue(_text, "three"), CancellationToken.None))!;
        Assert.Equal(2, source.Reads);

        var plain = await reader.DecryptAsync(_scope, _text, later, CancellationToken.None);

        Assert.Equal("three", plain.GetValue<string>());
        Assert.Equal(3, source.Reads);
    }

    [Fact]
    public async Task A_value_that_names_a_key_the_tenant_lacks_does_not_decrypt()
    {
        var cipher = CipherWith(Key(TenantKeyKind.Data, 1), Key(TenantKeyKind.Lookup, 2));
        var stored = (await cipher.EncryptAsync(_scope, new RecordValue(_text, "secret"), CancellationToken.None))!;

        foreach (var number in new[] { 2, 3, 0, -1 })
        {
            var named = (byte[])stored.Clone();
            BinaryPrimitives.WriteInt32BigEndian(named, number);
            await AssertFailsAsync(cipher, _scope, _text, named);
        }
    }

    [Fact]
    public async Task No_message_holds_the_plain_value()
    {
        const string text = "very-secret-text";
        var cipher = new SensitiveValues(new TenantKeys(new FakeSource(Key(TenantKeyKind.Data, 1))));
        var stored = (await cipher.EncryptAsync(_scope, new RecordValue(_text, text), CancellationToken.None))!;
        var moved = _scope with { RecordId = Guid.NewGuid() };

        var failure = await Assert.ThrowsAsync<SensitiveValueDecryptionException>(
            () => cipher.DecryptAsync(moved, _text, stored, CancellationToken.None));
        var missing = await Assert.ThrowsAsync<TenantDataKeyMissingException>(
            () => new SensitiveValues(new TenantKeys(new FakeSource())).EncryptAsync(_scope, new RecordValue(_text, text), CancellationToken.None));

        Assert.DoesNotContain(text, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(text, missing.Message, StringComparison.Ordinal);
        Assert.Contains(Tenant, failure.Message, StringComparison.Ordinal);
    }

    private static async Task AssertFailsAsync(SensitiveValues cipher, SensitiveValueScope scope, FieldModel field, byte[] stored) =>
        await Assert.ThrowsAsync<SensitiveValueDecryptionException>(() => cipher.DecryptAsync(scope, field, stored, CancellationToken.None));

    private static FieldModel FieldOf(FieldType type) =>
        type switch
        {
            FieldType.Decimal => Models.Field("amount", type, precision: 10, scale: 2),
            FieldType.Money => Models.Field("amount", type, precision: 10, scale: 2),
            FieldType.Enum => Models.Field("status", type, values: ["open", "closed"]),
            _ => Models.Field("value", type),
        };

    private static SensitiveValues CipherWith(params TenantKey[] keys) => new(new TenantKeys(new FakeSource(keys)));

    private static TenantKey Key(TenantKeyKind kind, int number) => new(kind, number, Enumerable.Repeat((byte)(number * 7 + (int)kind), TenantKey.Size).ToArray());

    private sealed class FakeSource(params TenantKey[] initial) : ITenantKeySource
    {
        private readonly List<TenantKey> _keys = [.. initial];

        public int Reads { get; private set; }

        public void Add(TenantKey key) => _keys.Add(key);

        public Task<IReadOnlyList<TenantKey>> ReadAsync(string tenantId, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<TenantKey>>(tenantId == Tenant ? [.. _keys] : []);
        }
    }
}
