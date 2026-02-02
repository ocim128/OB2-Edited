namespace RuriLib.Blocks.Utility.PairTrading
{
    /// <summary>
    /// Wavelet decomposition type.
    /// </summary>
    public enum WaveletType
    {
        Haar,
        Db4,
        Coif2
    }

    /// <summary>
    /// Result of copula dependence analysis.
    /// </summary>
    internal readonly struct CopulaResult
    {
        public CopulaResult(double kendallTau, double tailUpper, double tailLower, string copulaType, double opportunityScore)
        {
            KendallTau = kendallTau;
            TailUpper = tailUpper;
            TailLower = tailLower;
            CopulaType = copulaType;
            OpportunityScore = opportunityScore;
        }

        public double KendallTau { get; }
        public double TailUpper { get; }
        public double TailLower { get; }
        public string CopulaType { get; }
        public double OpportunityScore { get; }
    }

    /// <summary>
    /// Result of wavelet decomposition analysis.
    /// </summary>
    internal readonly struct WaveletResult
    {
        public WaveletResult(int dominantCycle, double noiseRatio, double spreadZScore)
        {
            DominantCycle = dominantCycle;
            NoiseRatio = noiseRatio;
            SpreadZScore = spreadZScore;
        }

        public int DominantCycle { get; }
        public double NoiseRatio { get; }
        public double SpreadZScore { get; }
    }

    /// <summary>
    /// Result of transfer entropy analysis.
    /// </summary>
    internal readonly struct TransferEntropyResult
    {
        public TransferEntropyResult(double te1To2, double te2To1, double netFlow, string leadingAsset, int lagBars, double significance)
        {
            Te1To2 = te1To2;
            Te2To1 = te2To1;
            NetFlow = netFlow;
            LeadingAsset = leadingAsset;
            LagBars = lagBars;
            Significance = significance;
        }

        public double Te1To2 { get; }
        public double Te2To1 { get; }
        public double NetFlow { get; }
        public string LeadingAsset { get; }
        public int LagBars { get; }
        public double Significance { get; }
    }

    /// <summary>
    /// Wavelet level data during decomposition.
    /// </summary>
    internal sealed class WaveletLevel
    {
        public int Scale { get; set; }
        public double[] Approximation { get; set; } = [];
        public double[] Detail { get; set; } = [];
        public double Energy { get; set; }
    }

    /// <summary>
    /// Wavelet filter coefficients.
    /// </summary>
    internal readonly struct WaveletFilters
    {
        public WaveletFilters(double[] loD, double[] hiD, double[] loR, double[] hiR)
        {
            LoD = loD;
            HiD = hiD;
            LoR = loR;
            HiR = hiR;
        }

        public double[] LoD { get; }
        public double[] HiD { get; }
        public double[] LoR { get; }
        public double[] HiR { get; }
    }
}
