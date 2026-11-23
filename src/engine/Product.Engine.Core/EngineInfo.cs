namespace Product.Engine.Core;

// Fixed facts about this engine build (document 25, sections 5.1 and 6.2).
public static class EngineInfo
{
    // The engine's semantic version. Every evaluation result carries it.
    // Keep it equal to VersionPrefix in Directory.Build.props. A test checks this.
    // A change in rule behavior is a new engine version (document 25, section 9).
    public const string Version = "0.1.0";

    // The snapshot schema major version this engine reads (document 25, section 6.2).
    // A snapshot with another major version is refused with snapshot.schema-unsupported.
    public const int SupportedSnapshotSchemaMajor = 1;
}
