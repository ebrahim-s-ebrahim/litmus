using System.Diagnostics;

namespace DotNetTestRadar.Tests.Integration;

/// <summary>
/// Creates a temporary git repository with realistic source files, a .slnx solution,
/// .csproj projects, git history, and a Cobertura coverage XML file.
/// Disposed after each test to clean up the temp directory.
/// </summary>
public class IntegrationTestFixture : IDisposable
{
    public string RootDir { get; }
    public string SolutionPath { get; }
    public string CoveragePath { get; }

    private const string SourceProjectName = "MyApp";
    private const string TestProjectName = "MyApp.Tests";

    public IntegrationTestFixture()
    {
        RootDir = Path.Combine(Path.GetTempPath(), $"testradar-integration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(RootDir);

        InitGitRepo();
        WriteSlnx();
        WriteSourceProject();
        WriteTestProject();
        WriteSourceFiles();
        GitCommit("Initial commit");

        // Second commit: modify OrderService to create churn
        ModifyOrderService();
        GitCommit("Add discount logic to OrderService");

        // Third commit: modify OrderService again for more churn weight
        AppendToOrderService();
        GitCommit("Add logging to OrderService");

        WriteCoverageXml();

        SolutionPath = Path.Combine(RootDir, "TestSolution.slnx");
        CoveragePath = Path.Combine(RootDir, "coverage.cobertura.xml");
    }

    private void InitGitRepo()
    {
        RunGit("init");
        RunGit("config user.name \"Test User\"");
        RunGit("config user.email \"test@example.com\"");
    }

    private void WriteSlnx()
    {
        var slnx = """
            <Solution>
              <Project Path="MyApp/MyApp.csproj" />
              <Project Path="MyApp.Tests/MyApp.Tests.csproj" />
            </Solution>
            """;
        File.WriteAllText(Path.Combine(RootDir, "TestSolution.slnx"), slnx);
    }

    private void WriteSourceProject()
    {
        var dir = Path.Combine(RootDir, SourceProjectName);
        Directory.CreateDirectory(Path.Combine(dir, "Services"));

        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """;
        File.WriteAllText(Path.Combine(dir, "MyApp.csproj"), csproj);
    }

    private void WriteTestProject()
    {
        var dir = Path.Combine(RootDir, TestProjectName);
        Directory.CreateDirectory(dir);

        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(Path.Combine(dir, "MyApp.Tests.csproj"), csproj);

        var testFile = """
            using System;

            namespace MyApp.Tests;

            public class SampleTest
            {
                public void Test1()
                {
                    var x = 1 + 1;
                }
            }
            """;
        File.WriteAllText(Path.Combine(dir, "SampleTest.cs"), testFile);
    }

    private void WriteSourceFiles()
    {
        var servicesDir = Path.Combine(RootDir, SourceProjectName, "Services");

        // SimpleService: low complexity, no infra calls
        var simple = """
            namespace MyApp.Services;

            public class SimpleService
            {
                public int Add(int a, int b)
                {
                    return a + b;
                }

                public string Greet(string name)
                {
                    return "Hello, " + name;
                }
            }
            """;
        File.WriteAllText(Path.Combine(servicesDir, "SimpleService.cs"), simple);

        // OrderService: high complexity + infra calls (DateTime.Now, new HttpClient)
        var order = """
            using System;
            using System.Net.Http;

            namespace MyApp.Services;

            public class OrderService
            {
                public decimal CalculateTotal(decimal price, int quantity, string customerType)
                {
                    var total = price * quantity;
                    var now = DateTime.Now;

                    if (customerType == "VIP")
                    {
                        if (quantity > 10)
                            total *= 0.8m;
                        else if (quantity > 5)
                            total *= 0.9m;
                        else
                            total *= 0.95m;
                    }
                    else if (customerType == "Wholesale")
                    {
                        if (quantity > 100)
                            total *= 0.7m;
                        else if (quantity > 50)
                            total *= 0.75m;
                    }

                    if (now.DayOfWeek == DayOfWeek.Friday)
                        total *= 0.95m;

                    using var client = new HttpClient();

                    for (int i = 0; i < quantity; i++)
                    {
                        if (i % 2 == 0)
                            total += 0.01m;
                    }

                    return total;
                }
            }
            """;
        File.WriteAllText(Path.Combine(servicesDir, "OrderService.cs"), order);

        // DataAccess: medium complexity, direct instantiation (new SqlConnection)
        var dataAccess = """
            using System;
            using System.Data;
            using System.Data.SqlClient;

            namespace MyApp.Services;

            public class DataAccess
            {
                public object GetById(int id)
                {
                    var connection = new SqlConnection("Server=localhost;Database=test");

                    if (id <= 0)
                        throw new ArgumentException("Invalid id");

                    if (id > 1000)
                        return null;

                    return new { Id = id, Name = "Item" };
                }
            }
            """;
        File.WriteAllText(Path.Combine(servicesDir, "DataAccess.cs"), dataAccess);
    }

    private void ModifyOrderService()
    {
        // Rewrite the file with an added method to create churn
        var path = Path.Combine(RootDir, SourceProjectName, "Services", "OrderService.cs");
        var content = """
            using System;
            using System.Net.Http;

            namespace MyApp.Services;

            public class OrderService
            {
                public decimal CalculateTotal(decimal price, int quantity, string customerType)
                {
                    var total = price * quantity;
                    var now = DateTime.Now;

                    if (customerType == "VIP")
                    {
                        if (quantity > 10)
                            total *= 0.8m;
                        else if (quantity > 5)
                            total *= 0.9m;
                        else
                            total *= 0.95m;
                    }
                    else if (customerType == "Wholesale")
                    {
                        if (quantity > 100)
                            total *= 0.7m;
                        else if (quantity > 50)
                            total *= 0.75m;
                    }

                    if (now.DayOfWeek == DayOfWeek.Friday)
                        total *= 0.95m;

                    using var client = new HttpClient();

                    for (int i = 0; i < quantity; i++)
                    {
                        if (i % 2 == 0)
                            total += 0.01m;
                    }

                    return total;
                }

                public decimal ApplyDiscount(decimal total, decimal discountPercent)
                {
                    if (discountPercent < 0 || discountPercent > 100)
                        throw new ArgumentException("Invalid discount");

                    if (discountPercent == 0)
                        return total;

                    return total * (1 - discountPercent / 100);
                }
            }
            """;
        File.WriteAllText(path, content);
    }

    private void AppendToOrderService()
    {
        // Rewrite again with logging added to create more churn
        var path = Path.Combine(RootDir, SourceProjectName, "Services", "OrderService.cs");
        var content = """
            using System;
            using System.Net.Http;

            namespace MyApp.Services;

            public class OrderService
            {
                public decimal CalculateTotal(decimal price, int quantity, string customerType)
                {
                    var total = price * quantity;
                    var now = DateTime.Now;

                    if (customerType == "VIP")
                    {
                        if (quantity > 10)
                            total *= 0.8m;
                        else if (quantity > 5)
                            total *= 0.9m;
                        else
                            total *= 0.95m;
                    }
                    else if (customerType == "Wholesale")
                    {
                        if (quantity > 100)
                            total *= 0.7m;
                        else if (quantity > 50)
                            total *= 0.75m;
                    }

                    if (now.DayOfWeek == DayOfWeek.Friday)
                        total *= 0.95m;

                    using var client = new HttpClient();

                    for (int i = 0; i < quantity; i++)
                    {
                        if (i % 2 == 0)
                            total += 0.01m;
                    }

                    return total;
                }

                public decimal ApplyDiscount(decimal total, decimal discountPercent)
                {
                    if (discountPercent < 0 || discountPercent > 100)
                        throw new ArgumentException("Invalid discount");

                    if (discountPercent == 0)
                        return total;

                    var result = total * (1 - discountPercent / 100);
                    var timestamp = DateTime.Now;
                    Console.WriteLine($"Discount applied at {timestamp}: {discountPercent}%");
                    return result;
                }
            }
            """;
        File.WriteAllText(path, content);
    }

    private void WriteCoverageXml()
    {
        // Build absolute paths using forward slashes (as Cobertura tools do)
        var servicesDir = Path.Combine(RootDir, SourceProjectName, "Services").Replace('\\', '/');

        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <coverage version="1" timestamp="1700000000" line-rate="0.5" branch-rate="0.5">
              <packages>
                <package name="MyApp">
                  <classes>
                    <class name="MyApp.Services.SimpleService" filename="{servicesDir}/SimpleService.cs" line-rate="0.90">
                      <lines>
                        <line number="5" hits="10" />
                      </lines>
                    </class>
                    <class name="MyApp.Services.OrderService" filename="{servicesDir}/OrderService.cs" line-rate="0.10">
                      <lines>
                        <line number="10" hits="1" />
                      </lines>
                    </class>
                    <class name="MyApp.Services.DataAccess" filename="{servicesDir}/DataAccess.cs" line-rate="0.50">
                      <lines>
                        <line number="10" hits="5" />
                      </lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """;
        File.WriteAllText(Path.Combine(RootDir, "coverage.cobertura.xml"), xml);
    }

    private void GitCommit(string message)
    {
        RunGit("add -A");
        RunGit($"commit -m \"{message}\"");
    }

    private void RunGit(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = RootDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var stderr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"git {arguments} failed: {stderr}");
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootDir))
                Directory.Delete(RootDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; temp dir will be cleaned eventually
        }
    }
}
