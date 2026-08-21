namespace PhotoIdentity.Api;

public readonly record struct ArchiveAdvancementWorkClassification(
    bool HasWork,
    bool WaitingForOneDrive);

public static class ArchiveAdvancementWorkClassifier
{
    public static ArchiveAdvancementWorkClassification Classify(
        bool hasRunnableWork,
        bool hasOneDriveTransition)
    {
        return new ArchiveAdvancementWorkClassification(
            HasWork: hasRunnableWork || hasOneDriveTransition,
            WaitingForOneDrive: hasOneDriveTransition && !hasRunnableWork);
    }
}
