using System.Security.Cryptography;
using Axis.Tenancy.Keys;
using Axis.Tenancy.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Axis.Tenancy.Tests;

public sealed class KeyServiceTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("axis-key-service-tests-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void The_installation_key_loads_from_wrapped_settings_that_hold_neither_plain_key()
    {
        var keyFileBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var keyService = KeyServiceOf(WriteKeyFile(keyFileBase64));
        var plain = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var previous = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var wrapped = keyService.Wrap(KeyService.InstallationKey, plain);
        var wrappedPrevious = keyService.Wrap(KeyService.InstallationKey, previous);

        var cipher = TenantSecretCipher.Load(Settings(keyFileBase64, wrapped, wrappedPrevious));

        Assert.DoesNotContain(plain, wrapped, StringComparison.Ordinal);
        Assert.DoesNotContain(previous, wrappedPrevious, StringComparison.Ordinal);
        Assert.True(cipher.HasPreviousKey);
        var stored = cipher.Encrypt("a", TenantSecretFields.RuntimePassword, "p4ssw0rd");
        Assert.Equal("p4ssw0rd", cipher.Decrypt("a", TenantSecretFields.RuntimePassword, stored).Reveal());
        Assert.Equal("p4ssw0rd", TenantSecretCipher.FromKey(Convert.FromBase64String(plain)).Decrypt("a", TenantSecretFields.RuntimePassword, stored).Reveal());
        var old = TenantSecretCipher.FromKey(Convert.FromBase64String(previous)).Encrypt("a", TenantSecretFields.RuntimePassword, "old");
        Assert.Equal("old", cipher.Decrypt("a", TenantSecretFields.RuntimePassword, old).Reveal());
    }

    [Fact]
    public void A_blank_previous_key_is_not_set_with_a_key_service()
    {
        var keyFileBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var wrapped = KeyServiceOf(WriteKeyFile(keyFileBase64)).Wrap(KeyService.InstallationKey, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

        Assert.False(TenantSecretCipher.Load(Settings(keyFileBase64, wrapped, "")).HasPreviousKey);
    }

    [Fact]
    public void A_wrapped_value_is_made_of_a_nonce_the_key_and_a_tag_and_differs_each_time()
    {
        var keyService = KeyServiceOf(WriteKeyFile(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

        var first = keyService.Wrap(KeyService.InstallationKey, "abc");
        var second = keyService.Wrap(KeyService.InstallationKey, "abc");

        Assert.Equal(3 + TenantSecretCipher.NonceSize + TenantSecretCipher.TagSize, Convert.FromBase64String(first).Length);
        Assert.NotEqual(first, second);
        Assert.Equal("abc", keyService.Unwrap(KeyService.InstallationKey, "Any:Setting", first).Reveal());
    }

    [Theory]
    [InlineData("wrong-purpose")]
    [InlineData("another-key-file")]
    [InlineData("plain-key")]
    [InlineData("not-base64")]
    [InlineData("too-short")]
    public void A_value_that_does_not_unwrap_is_refused_with_a_message_that_names_the_setting_and_holds_no_key_or_path(string case_)
    {
        var keyFileBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var keyFile = WriteKeyFile(keyFileBase64);
        var keyService = KeyServiceOf(keyFile);
        var plain = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var value = case_ switch
        {
            "wrong-purpose" => keyService.Wrap(KeyService.AgentHostKey, plain),
            "another-key-file" => KeyServiceOf(WriteKeyFile(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), "other")).Wrap(KeyService.InstallationKey, plain),
            "plain-key" => plain,
            "not-base64" => "this is not base64 !!",
            _ => Convert.ToBase64String(new byte[10]),
        };

        var exception = Assert.Throws<InvalidOperationException>(() => TenantSecretCipher.Load(Settings(keyFileBase64, value, null)));

        Assert.Equal("The setting Platform:SecretKey does not unwrap with the key of KeyService:KeyFile.", exception.Message);
        Assert.Null(exception.InnerException);
        var text = exception.ToString();
        foreach (var hidden in new[] { plain, keyFileBase64, value, keyFile, _folder })
        {
            Assert.DoesNotContain(hidden, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_previous_key_that_does_not_unwrap_names_its_own_setting()
    {
        var keyFileBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var keyService = KeyServiceOf(WriteKeyFile(keyFileBase64));
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var wrapped = keyService.Wrap(KeyService.InstallationKey, key);

        var exception = Assert.Throws<InvalidOperationException>(() => TenantSecretCipher.Load(Settings(keyFileBase64, wrapped, key)));

        Assert.Equal("The setting Platform:PreviousSecretKey does not unwrap with the key of KeyService:KeyFile.", exception.Message);
    }

    [Fact]
    public void An_unwrapped_value_still_has_to_be_a_key_of_32_bytes()
    {
        var keyFileBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var wrapped = KeyServiceOf(WriteKeyFile(keyFileBase64)).Wrap(KeyService.InstallationKey, Convert.ToBase64String(new byte[16]));

        var exception = Assert.Throws<InvalidOperationException>(() => TenantSecretCipher.Load(Settings(keyFileBase64, wrapped, null)));

        Assert.Equal("The setting Platform:SecretKey must be 32 bytes in base64.", exception.Message);
    }

    [Fact]
    public void An_unknown_kind_names_its_setting_and_not_its_value()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => KeyService.Load(Configure(("KeyService:Kind", "vault-of-secrets"), ("KeyService:KeyFile", "x"))));

        Assert.Equal("The setting KeyService:Kind must be file or aws-kms.", exception.Message);
        Assert.DoesNotContain("vault-of-secrets", exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void The_aws_kms_kind_without_a_key_id_names_that_setting(string? keyId)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => KeyService.Load(Configure(("KeyService:Kind", "aws-kms"), ("KeyService:KeyId", keyId))));

        Assert.Equal("The setting KeyService:KeyId is required.", exception.Message);
    }

    [Fact]
    public void A_missing_or_unreadable_or_malformed_key_file_names_its_setting_and_not_its_path()
    {
        var malformed = Path.Combine(_folder, "malformed");
        File.WriteAllText(malformed, "not a key");
        var missing = Path.Combine(_folder, "missing");

        Assert.Equal("The setting KeyService:KeyFile is required.", Assert.Throws<InvalidOperationException>(() => KeyService.Load(Configure(("KeyService:Kind", "file")))).Message);
        var unreadable = Assert.Throws<InvalidOperationException>(() => KeyService.Load(Configure(("KeyService:Kind", "file"), ("KeyService:KeyFile", missing))));
        Assert.Equal("The KeyService:KeyFile file cannot be read.", unreadable.Message);
        var bad = Assert.Throws<InvalidOperationException>(() => KeyService.Load(Configure(("KeyService:Kind", "file"), ("KeyService:KeyFile", malformed))));
        Assert.Equal("The KeyService:KeyFile file does not hold a key of 32 bytes in base64.", bad.Message);
        Assert.DoesNotContain(_folder, unreadable.ToString() + bad, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => TenantSecretCipher.Load(Configure(("KeyService:Kind", "file"), ("Platform:SecretKey", Convert.ToBase64String(new byte[32])))));
    }

    [Fact]
    public void Without_a_kind_there_is_no_key_service_and_plain_keys_load_as_before()
    {
        var plain = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        Assert.Null(KeyService.Load(Configure()));
        Assert.Null(KeyService.Load(Configure(("KeyService:Kind", " "), ("KeyService:KeyFile", "ignored"))));
        var cipher = TenantSecretCipher.Load(Configure(("Platform:SecretKey", plain)));
        var stored = cipher.Encrypt("a", TenantSecretFields.RuntimePassword, "p4ssw0rd");
        Assert.Equal("p4ssw0rd", TenantSecretCipher.FromKey(Convert.FromBase64String(plain)).Decrypt("a", TenantSecretFields.RuntimePassword, stored).Reveal());
    }

    [Fact]
    public void A_wrapped_key_does_not_decrypt_as_a_tenant_field()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var cipher = TenantSecretCipher.FromKey(key);

        var wrapped = cipher.WrapKey(KeyService.InstallationKey, "abc");

        Assert.Throws<InvalidOperationException>(() => cipher.Decrypt("a", TenantSecretFields.WrappedKey(KeyService.InstallationKey), wrapped));
        Assert.Equal("abc", cipher.UnwrapKey(KeyService.InstallationKey, wrapped, "failed").Reveal());
        Assert.Equal("failed", Assert.Throws<InvalidOperationException>(() => cipher.UnwrapKey(KeyService.AgentHostKey, wrapped, "failed")).Message);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void A_host_outside_development_and_testing_refuses_a_missing_key_service_and_names_the_setting_and_not_the_key(string environment)
    {
        var plain = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var settings = Configure(("Platform:SecretKey", plain));

        var forKeyService = Assert.Throws<InvalidOperationException>(() => KeyService.Load(settings, Host(environment)));
        var forCipher = Assert.Throws<InvalidOperationException>(() => TenantSecretCipher.Load(settings, Host(environment)));

        Assert.Equal("The setting KeyService:Kind is required outside Development and Testing.", forKeyService.Message);
        Assert.Equal(forKeyService.Message, forCipher.Message);
        Assert.DoesNotContain(plain, forCipher.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_host_outside_development_and_testing_refuses_a_plain_installation_key_with_a_key_service_and_names_the_setting()
    {
        var keyFileBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var plain = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var exception = Assert.Throws<InvalidOperationException>(() => TenantSecretCipher.Load(Settings(keyFileBase64, plain, null), Host("Production")));

        Assert.Contains("Platform:SecretKey", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(plain, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_folder, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_host_outside_development_and_testing_loads_a_wrapped_installation_key()
    {
        var keyFileBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var plain = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var wrapped = KeyServiceOf(WriteKeyFile(keyFileBase64)).Wrap(KeyService.InstallationKey, plain);

        var cipher = TenantSecretCipher.Load(Settings(keyFileBase64, wrapped, null), Host("Production"));

        var stored = cipher.Encrypt("a", TenantSecretFields.RuntimePassword, "p4ssw0rd");
        Assert.Equal("p4ssw0rd", TenantSecretCipher.FromKey(Convert.FromBase64String(plain)).Decrypt("a", TenantSecretFields.RuntimePassword, stored).Reveal());
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void A_development_or_testing_host_loads_a_plain_installation_key_with_no_key_service(string environment)
    {
        var plain = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var settings = Configure(("Platform:SecretKey", plain));

        Assert.Null(KeyService.Load(settings, Host(environment)));
        var cipher = TenantSecretCipher.Load(settings, Host(environment));
        var stored = cipher.Encrypt("a", TenantSecretFields.RuntimePassword, "p4ssw0rd");
        Assert.Equal("p4ssw0rd", TenantSecretCipher.FromKey(Convert.FromBase64String(plain)).Decrypt("a", TenantSecretFields.RuntimePassword, stored).Reveal());
    }

    private static StubEnvironment Host(string environment) => new StubEnvironment(environment);

    private string WriteKeyFile(string base64, string name = "key-file")
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, base64 + "\n");
        return path;
    }

    private static IKeyService KeyServiceOf(string keyFile) =>
        KeyService.Load(Configure(("KeyService:Kind", "file"), ("KeyService:KeyFile", keyFile)))!;

    private IConfiguration Settings(string keyFileBase64, string key, string? previous)
    {
        var keyFile = Path.Combine(_folder, "key-file");
        File.WriteAllText(keyFile, keyFileBase64 + "\n");
        return Configure(("KeyService:Kind", "file"), ("KeyService:KeyFile", keyFile), ("Platform:SecretKey", key), ("Platform:PreviousSecretKey", previous));
    }

    private static IConfiguration Configure(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value))).Build();

    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Axis.Tenancy.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
