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

    /// <summary>
    /// Result of correlation velocity analysis.
    /// </summary>
    internal readonly struct CorrelationVelocityResult
    {
        public CorrelationVelocityResult(double currentCorrelation, double previousCorrelation, double velocity, double acceleration, string regime)
        {
            CurrentCorrelation = currentCorrelation;
            PreviousCorrelation = previousCorrelation;
            Velocity = velocity;
            Acceleration = acceleration;
            Regime = regime;
        }

        /// <summary>Current rolling correlation value.</summary>
        public double CurrentCorrelation { get; }
        
        /// <summary>Correlation value from velocityLookback periods ago.</summary>
        public double PreviousCorrelation { get; }
        
        /// <summary>Rate of change in correlation per period. Positive = strengthening, Negative = weakening.</summary>
        public double Velocity { get; }
        
        /// <summary>Rate of change of velocity. Positive = accelerating, Negative = decelerating.</summary>
        public double Acceleration { get; }
        
        /// <summary>
        /// Current correlation regime: stable_strong, stable_weak, stable, 
        /// strengthening, recovering, weakening, breaking_down
        /// </summary>
        public string Regime { get; }
    }

    /// <summary>
    /// Result of volatility-adjusted spread analysis.
    /// </summary>
    internal readonly struct VolatilityAdjustedSpreadResult
    {
        public VolatilityAdjustedSpreadResult(
            double rawZScore, double adjustedZScore, double combinedVolatility,
            double primaryVolatility, double secondaryVolatility, double signalStrength, string signalQuality)
        {
            RawZScore = rawZScore;
            AdjustedZScore = adjustedZScore;
            CombinedVolatility = combinedVolatility;
            PrimaryVolatility = primaryVolatility;
            SecondaryVolatility = secondaryVolatility;
            SignalStrength = signalStrength;
            SignalQuality = signalQuality;
        }

        /// <summary>Standard spread Z-score without volatility adjustment.</summary>
        public double RawZScore { get; }
        
        /// <summary>Z-score amplified when volatility is low (stronger signal).</summary>
        public double AdjustedZScore { get; }
        
        /// <summary>Combined volatility of both assets (RMS of individual volatilities).</summary>
        public double CombinedVolatility { get; }
        
        /// <summary>Primary asset volatility (std dev of returns).</summary>
        public double PrimaryVolatility { get; }
        
        /// <summary>Secondary asset volatility (std dev of returns).</summary>
        public double SecondaryVolatility { get; }
        
        /// <summary>Signal strength score 0-100 based on adjusted Z-score.</summary>
        public double SignalStrength { get; }
        
        /// <summary>Signal quality: premium, strong, moderate, weak, noisy, insufficient_data.</summary>
        public string SignalQuality { get; }
    }
}
