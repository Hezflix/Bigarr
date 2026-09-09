namespace Bigarr.Domain.History;

public enum HistoryEventType
{
    Grabbed = 1,
    DownloadFailed = 2,
    Imported = 3,
    Upgraded = 4,
    ImportFailed = 5,
    FileDeleted = 6,
    Renamed = 7,
    Blocklisted = 8,
    DefaultChanged = 9,
}
