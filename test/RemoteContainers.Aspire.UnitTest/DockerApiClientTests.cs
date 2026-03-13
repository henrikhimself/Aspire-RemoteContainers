using System.Collections;
using System.Net;
using System.Text;
using Hj.RemoteContainers.Aspire.Docker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hj.RemoteContainers.Aspire.UnitTest;

public sealed class DockerApiClientTests
{
  [Fact]
  public async Task GetAllContainerHostPortsAsync_WhenContainerFoundOnFirstPoll_ReturnsPortsAfterStabilisation()
  {
    // Arrange
    var f = Fixture.Create(
      ContainerJson("abc", ["/my-resource"], [8080u]),
      ContainerJson("abc", ["/my-resource"], [8080u]));

    // Act
    var result = await f.Sut.GetAllContainerHostPortsAsync("my-resource", CancellationToken.None);

    // Assert
    Assert.Equal([8080u], result);
  }

  [Fact]
  public async Task GetAllContainerHostPortsAsync_WhenContainerNotFoundThenFound_ReturnsPortsImmediately()
  {
    // Arrange
    var f = Fixture.Create(
      EmptyContainersJson(),
      ContainerJson("abc", ["/my-resource"], [8080u]));

    // Act
    var result = await f.Sut.GetAllContainerHostPortsAsync("my-resource", CancellationToken.None);

    // Assert
    Assert.Equal([8080u], result);
  }

  [Fact]
  public async Task GetAllContainerHostPortsAsync_WhenContainerIdChangesBetweenPolls_WaitsForStabilisation()
  {
    // Arrange — first poll sees stale container, second sees new ID, third confirms new ID
    var f = Fixture.Create(
      ContainerJson("stale-id", ["/my-resource"], [9090u]),
      ContainerJson("new-id", ["/my-resource"], [8080u]),
      ContainerJson("new-id", ["/my-resource"], [8080u]));

    // Act
    var result = await f.Sut.GetAllContainerHostPortsAsync("my-resource", CancellationToken.None);

    // Assert
    Assert.Equal([8080u], result);
  }

  [Fact]
  public async Task GetAllContainerHostPortsAsync_WhenTimeoutExpires_ReturnsNull()
  {
    // Arrange — tiny timeout so the test completes quickly
    var f = Fixture.Create(
      timeout: TimeSpan.FromMilliseconds(50),
      pollInterval: TimeSpan.FromMilliseconds(1),
      responses: Enumerable.Repeat(EmptyContainersJson(), 200).ToArray());

    // Act
    var result = await f.Sut.GetAllContainerHostPortsAsync("my-resource", CancellationToken.None);

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task GetAllContainerHostPortsAsync_WhenHttpRequestFails_RetriesAndEventuallyReturnsPorts()
  {
    // Arrange — null signals the handler to throw HttpRequestException
    var f = Fixture.Create(
      null,
      ContainerJson("abc", ["/my-resource"], [8080u]));

    // Act
    var result = await f.Sut.GetAllContainerHostPortsAsync("my-resource", CancellationToken.None);

    // Assert
    Assert.Equal([8080u], result);
  }

  [Fact]
  public async Task GetAllContainerHostPortsAsync_WhenNameHasLeadingSlash_MatchesResourceName()
  {
    // Arrange
    var f = Fixture.Create(
      EmptyContainersJson(),
      ContainerJson("abc", ["/my-resource"], [8080u]));

    // Act
    var result = await f.Sut.GetAllContainerHostPortsAsync("my-resource", CancellationToken.None);

    // Assert
    Assert.NotNull(result);
  }

  [Fact]
  public async Task GetAllContainerHostPortsAsync_WhenNameMatchesByPrefix_ReturnsPorts()
  {
    // Arrange — Docker name includes an instance suffix after the resource name
    var f = Fixture.Create(
      EmptyContainersJson(),
      ContainerJson("abc", ["/my-resource-abc123def456"], [8080u]));

    // Act
    var result = await f.Sut.GetAllContainerHostPortsAsync("my-resource", CancellationToken.None);

    // Assert
    Assert.NotNull(result);
  }

  [Fact]
  public async Task GetAllContainerHostPortsAsync_WhenNameMatchingIsCaseInsensitive_ReturnsPorts()
  {
    // Arrange
    var f = Fixture.Create(
      EmptyContainersJson(),
      ContainerJson("abc", ["/MY-RESOURCE"], [8080u]));

    // Act
    var result = await f.Sut.GetAllContainerHostPortsAsync("my-resource", CancellationToken.None);

    // Assert
    Assert.NotNull(result);
  }

  [Fact]
  public async Task GetAllContainerHostPortsAsync_WhenContainerHasMultiplePorts_ReturnsAllPorts()
  {
    // Arrange
    var f = Fixture.Create(
      EmptyContainersJson(),
      ContainerJson("abc", ["/my-resource"], [8080u, 5432u, 443u]));

    // Act
    var result = await f.Sut.GetAllContainerHostPortsAsync("my-resource", CancellationToken.None);

    // Assert
    Assert.NotNull(result);
    Assert.Equal(3, result.Count);
    Assert.Contains(8080u, result);
    Assert.Contains(5432u, result);
    Assert.Contains(443u, result);
  }

  [Fact]
  public async Task GetAllContainerHostPortsAsync_WhenContainerHasDuplicatePorts_DeduplicatesPorts()
  {
    // Arrange
    var f = Fixture.Create(
      EmptyContainersJson(),
      ContainerJson("abc", ["/my-resource"], [8080u, 8080u]));

    // Act
    var result = await f.Sut.GetAllContainerHostPortsAsync("my-resource", CancellationToken.None);

    // Assert
    Assert.Equal([8080u], result);
  }

  [Fact]
  public async Task GetAllContainerHostPortsAsync_WhenContainerNameDoesNotMatch_TimesOutAndReturnsNull()
  {
    // Arrange
    var f = Fixture.Create(
      timeout: TimeSpan.FromMilliseconds(50),
      pollInterval: TimeSpan.FromMilliseconds(1),
      responses: Enumerable.Repeat(ContainerJson("abc", ["/other-resource"], [8080u]), 200).ToArray());

    // Act
    var result = await f.Sut.GetAllContainerHostPortsAsync("my-resource", CancellationToken.None);

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task GetAllContainerHostPortsAsync_WhenContainerHasZeroPorts_SkipsContainerAndTimesOut()
  {
    // Arrange
    var f = Fixture.Create(
      timeout: TimeSpan.FromMilliseconds(50),
      pollInterval: TimeSpan.FromMilliseconds(1),
      responses: Enumerable.Repeat(ContainerJsonNoPorts("abc", "/my-resource"), 200).ToArray());

    // Act
    var result = await f.Sut.GetAllContainerHostPortsAsync("my-resource", CancellationToken.None);

    // Assert
    Assert.Null(result);
  }

  private static string ContainerJson(string id, string[] names, uint[] publicPorts)
  {
    var namesJson = string.Join(",", names.Select(n => $"\"{n}\""));
    var portsJson = string.Join(",", publicPorts.Select(p => $"{{\"PublicPort\":{p}}}"));

    return $"[{{\"Id\":\"{id}\",\"Names\":[{namesJson}],\"Ports\":[{portsJson}]}}]";
  }

  private static string ContainerJsonNoPorts(string id, string name) =>
    $"[{{\"Id\":\"{id}\",\"Names\":[\"{name}\"],\"Ports\":[]}}]";

  private static string EmptyContainersJson() => "[]";

  private sealed record Fixture(DockerApiClient Sut)
  {
    public static Fixture Create(params string?[] responses) =>
      Create(timeout: null, pollInterval: null, responses: responses);

    public static Fixture Create(
      TimeSpan? timeout = null,
      TimeSpan? pollInterval = null,
      params string?[] responses)
    {
      var handler = new FakeHttpHandler(responses);
      var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://docker/") };

      var configValues = new Dictionary<string, string?>
      {
        ["RemoteContainers:ContainerStartTimeout"] = (timeout ?? TimeSpan.FromSeconds(5)).ToString(),
        ["RemoteContainers:ContainerPollInterval"] = (pollInterval ?? TimeSpan.FromMilliseconds(5)).ToString(),
      };
      var config = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
      var appConfig = new AppConfiguration(config, "user", "/home/user", new Hashtable());
      var logger = Substitute.For<ILogger<DockerApiClient>>();

      return new Fixture(new DockerApiClient(logger, httpClient, appConfig));
    }
  }

  private sealed class FakeHttpHandler(params string?[] responses) : HttpMessageHandler
  {
    private readonly Queue<string?> _responses = new(responses);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      var response = _responses.Count > 0 ? _responses.Dequeue() : "[]";

      if (response is null)
      {
        throw new HttpRequestException("Simulated network error");
      }

      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent(response, Encoding.UTF8, "application/json"),
      });
    }
  }
}
