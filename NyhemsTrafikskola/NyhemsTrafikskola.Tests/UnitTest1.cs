namespace NyhemsTrafikskola.Tests;

public class TestEnvironmentTests
{
    [Test]
    public void TestEnvironment_IsReady()
    {
        var workingDirectory = Environment.CurrentDirectory;

        Assert.That(workingDirectory, Is.Not.Empty);
        Assert.That(Path.GetFileName(workingDirectory), Is.Not.Empty.Or.Null);
    }
}