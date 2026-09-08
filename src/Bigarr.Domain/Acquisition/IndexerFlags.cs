namespace Bigarr.Domain.Acquisition;

/// <summary>Bit values follow Radarr so the compat layer can expose <c>indexerFlags</c> unchanged.</summary>
[Flags]
public enum IndexerFlags
{
    None = 0,
    Freeleech = 1,
    Halfleech = 2,
    DoubleUpload = 4,
    Internal = 32,
    Scene = 128,
    Freeleech75 = 256,
    Freeleech25 = 512,
    Nuked = 2048,
}
