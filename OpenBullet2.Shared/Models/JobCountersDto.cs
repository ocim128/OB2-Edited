namespace OpenBullet2.Shared.Models;

public record JobCountersDto(int Hits, int Custom, int ToCheck, int Fails, int Bots, double Cpm, double Progress);
