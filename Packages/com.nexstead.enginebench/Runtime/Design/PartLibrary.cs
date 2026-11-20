// Ported from engine-sim (https://github.com/ange-yaghi/engine-sim): es/part-library/parts (cam_lobes.mr, camshafts.mr,
// heads.mr, intakes.mr, ignition_modules.mr), under generic names. MIT licence, Copyright (c) 2022 Ange Yaghi; see
// Third Party Notices.md.
using U = Nexstead.EngineBench.Design.ScriptUnits;

namespace Nexstead.EngineBench.Design
{
    /// <summary>A bank pair's camshafts, as the library's camshaft builders make them: an intake and an exhaust camshaft a bank.</summary>
    public sealed class CamshaftSet
    {
        public readonly CamshaftNode intakeCam0, intakeCam1, exhaustCam0, exhaustCam1;

        public CamshaftSet(CamshaftNode intakeCam0, CamshaftNode intakeCam1, CamshaftNode exhaustCam0, CamshaftNode exhaustCam1)
        {
            this.intakeCam0 = intakeCam0;
            this.intakeCam1 = intakeCam1;
            this.exhaustCam0 = exhaustCam0;
            this.exhaustCam1 = exhaustCam1;
        }
    }

    /// <summary>
    /// engine-sim's part library under generic names: cam lobes, camshaft sets, heads with measured flow, intakes and
    /// distributors. Each call builds new parts, as each use of a library node does in a script.
    /// </summary>
    public static class PartLibrary
    {
        const double Cycle = 2 * 360 * U.deg;

        // ---- Cam lobes ----

        /// <summary>A stock big-block V8's intake lobe: 194° at 50 thou, 390 thou lift.</summary>
        public static FunctionNode StockBigBlockIntakeLobe() =>
            ScriptFunctions.HarmonicCamLobe(durationAt50Thou: 194 * U.deg, gamma: 0.8, lift: 390 * U.thou, steps: 100);

        /// <summary>A stock big-block V8's exhaust lobe: 202° at 50 thou, 409 thou lift.</summary>
        public static FunctionNode StockBigBlockExhaustLobe() =>
            ScriptFunctions.HarmonicCamLobe(durationAt50Thou: 202 * U.deg, gamma: 0.8, lift: 409 * U.thou, steps: 100);

        /// <summary>A performance lobe: 232° at 50 thou, 578 thou lift.</summary>
        public static FunctionNode PerformanceLobe232() =>
            ScriptFunctions.HarmonicCamLobe(durationAt50Thou: 232 * U.deg, gamma: 0.75, lift: 578 * U.thou, steps: 100);

        /// <summary>A performance lobe: 252° at 50 thou, 612 thou lift.</summary>
        public static FunctionNode PerformanceLobe252() =>
            ScriptFunctions.HarmonicCamLobe(durationAt50Thou: 252 * U.deg, gamma: 0.8, lift: 612 * U.thou, steps: 100);

        // ---- Camshafts ----

        /// <summary>
        /// A cross-plane V8's camshafts for the firing order 1-8-4-3-6-5-7-2, cylinders numbered bank by bank: four lobes a
        /// camshaft, each lobe at the lobe centre from its cylinder's top dead centre. Lobe profiles default to
        /// <paramref name="lobeProfile"/>, the lobe centres to <paramref name="lobeSeparation"/>.
        /// </summary>
        public static CamshaftSet CrossPlaneV8Camshafts(FunctionNode lobeProfile = null, FunctionNode intakeLobeProfile = null,
            FunctionNode exhaustLobeProfile = null, double lobeSeparation = 114.0 * U.deg, double? intakeLobeCenter = null,
            double? exhaustLobeCenter = null, double advance = 0.0 * U.deg, double baseRadius = 0.75 * U.inch)
        {
            lobeProfile ??= StockBigBlockIntakeLobe();
            FunctionNode intakeProfile = intakeLobeProfile ?? lobeProfile;
            FunctionNode exhaustProfile = exhaustLobeProfile ?? lobeProfile;
            double intakeCenter = intakeLobeCenter ?? lobeSeparation;
            double exhaustCenter = exhaustLobeCenter ?? lobeSeparation;

            var parameters = new CamshaftParameters(advance: advance, baseRadius: baseRadius);
            var intakeCam0 = new CamshaftNode(parameters, lobeProfile: intakeProfile);
            var intakeCam1 = new CamshaftNode(parameters, lobeProfile: intakeProfile);
            var exhaustCam0 = new CamshaftNode(parameters, lobeProfile: exhaustProfile);
            var exhaustCam1 = new CamshaftNode(parameters, lobeProfile: exhaustProfile);

            const double rot90 = 90 * U.deg;
            const double rot360 = 360 * U.deg;
            exhaustCam0
                .AddLobe(rot360 - exhaustCenter)
                .AddLobe(rot360 - exhaustCenter + 3 * rot90)
                .AddLobe(rot360 - exhaustCenter + 5 * rot90)
                .AddLobe(rot360 - exhaustCenter + 6 * rot90);
            exhaustCam1
                .AddLobe(rot360 - exhaustCenter + 7 * rot90)
                .AddLobe(rot360 - exhaustCenter + 2 * rot90)
                .AddLobe(rot360 - exhaustCenter + 4 * rot90)
                .AddLobe(rot360 - exhaustCenter + 1 * rot90);
            intakeCam0
                .AddLobe(rot360 + intakeCenter)
                .AddLobe(rot360 + intakeCenter + 3 * rot90)
                .AddLobe(rot360 + intakeCenter + 5 * rot90)
                .AddLobe(rot360 + intakeCenter + 6 * rot90);
            intakeCam1
                .AddLobe(rot360 + intakeCenter + 7 * rot90)
                .AddLobe(rot360 + intakeCenter + 2 * rot90)
                .AddLobe(rot360 + intakeCenter + 4 * rot90)
                .AddLobe(rot360 + intakeCenter + 1 * rot90);
            return new CamshaftSet(intakeCam0, intakeCam1, exhaustCam0, exhaustCam1);
        }

        /// <summary>
        /// A V-twin's camshafts: one lobe a camshaft, the second cylinder's <paramref name="angle"/> of crank after the first's.
        /// </summary>
        public static CamshaftSet VTwinCamshafts(FunctionNode lobeProfile, FunctionNode intakeLobeProfile = null, FunctionNode exhaustLobeProfile = null,
            double lobeSeparation = 114.0 * U.deg, double? intakeLobeCenter = null, double? exhaustLobeCenter = null, double advance = 0.0 * U.deg,
            double baseRadius = 0.75 * U.inch, double angle = 90 * 3 * U.deg)
        {
            FunctionNode intakeProfile = intakeLobeProfile ?? lobeProfile;
            FunctionNode exhaustProfile = exhaustLobeProfile ?? lobeProfile;
            double intakeCenter = intakeLobeCenter ?? lobeSeparation;
            double exhaustCenter = exhaustLobeCenter ?? lobeSeparation;

            var parameters = new CamshaftParameters(advance: advance, baseRadius: baseRadius);
            var intakeCam0 = new CamshaftNode(parameters, lobeProfile: intakeProfile);
            var intakeCam1 = new CamshaftNode(parameters, lobeProfile: intakeProfile);
            var exhaustCam0 = new CamshaftNode(parameters, lobeProfile: exhaustProfile);
            var exhaustCam1 = new CamshaftNode(parameters, lobeProfile: exhaustProfile);

            const double rot360 = 360 * U.deg;
            exhaustCam0.AddLobe(rot360 - exhaustCenter);
            exhaustCam1.AddLobe(rot360 - exhaustCenter + angle);
            intakeCam0.AddLobe(rot360 + intakeCenter);
            intakeCam1.AddLobe(rot360 + intakeCenter + angle);
            return new CamshaftSet(intakeCam0, intakeCam1, exhaustCam0, exhaustCam1);
        }

        /// <summary>A 90° V-twin's camshafts: the second cylinder 270° of crank after the first.</summary>
        public static CamshaftSet VTwin90Camshafts(FunctionNode lobeProfile, FunctionNode intakeLobeProfile = null, FunctionNode exhaustLobeProfile = null,
            double lobeSeparation = 114.0 * U.deg, double? intakeLobeCenter = null, double? exhaustLobeCenter = null, double advance = 0.0 * U.deg,
            double baseRadius = 0.75 * U.inch) =>
            VTwinCamshafts(lobeProfile, intakeLobeProfile, exhaustLobeProfile, lobeSeparation, intakeLobeCenter, exhaustLobeCenter, advance, baseRadius,
                angle: 90 * 3 * U.deg);

        /// <summary>A stock big-block V8 camshaft set: the stock lobes, intake centre 108°, exhaust centre 113°.</summary>
        public static CamshaftSet StockBigBlockCamshaft() =>
            CrossPlaneV8Camshafts(advance: 0 * U.deg, intakeLobeProfile: StockBigBlockIntakeLobe(), exhaustLobeProfile: StockBigBlockExhaustLobe(),
                intakeLobeCenter: 108 * U.deg, exhaustLobeCenter: 113 * U.deg);

        /// <summary>A big-block V8 performance camshaft set: <see cref="PerformanceLobe232"/>, 110° lobe separation, 4° advanced.</summary>
        public static CamshaftSet PerformanceCamshaft232() =>
            CrossPlaneV8Camshafts(lobeProfile: PerformanceLobe232(), lobeSeparation: 110 * U.deg, advance: 4.0 * U.deg, baseRadius: 1000.0 * U.thou);

        /// <summary>A big-block V8 performance camshaft set: <see cref="PerformanceLobe252"/>, 110° lobe separation, 4° advanced.</summary>
        public static CamshaftSet PerformanceCamshaft252() =>
            CrossPlaneV8Camshafts(lobeProfile: PerformanceLobe252(), lobeSeparation: 110 * U.deg, advance: 4.0 * U.deg, baseRadius: 1000.0 * U.thou);

        // ---- Heads ----

        /// <summary>A stock big-block V8 head with small oval intake ports.</summary>
        public static CylinderHeadNode BigBlockOvalPortHead(CamshaftNode intakeCamshaft, CamshaftNode exhaustCamshaft, bool flipDisplay = false)
        {
            FunctionNode intakeFlow = new FunctionNode(50 * U.thou)
                .AddFlowSample(0, 0)
                .AddFlowSample(50, 25)
                .AddFlowSample(100, 75)
                .AddFlowSample(150, 100)
                .AddFlowSample(200, 130)
                .AddFlowSample(250, 180)
                .AddFlowSample(300, 190)
                .AddFlowSample(350, 220)
                .AddFlowSample(400, 240)
                .AddFlowSample(450, 250)
                .AddFlowSample(500, 260)
                .AddFlowSample(550, 260)
                .AddFlowSample(600, 260)
                .AddFlowSample(650, 255)
                .AddFlowSample(700, 250);
            FunctionNode exhaustFlow = new FunctionNode(50 * U.thou)
                .AddFlowSample(0, 0)
                .AddFlowSample(50, 25)
                .AddFlowSample(100, 50)
                .AddFlowSample(150, 75)
                .AddFlowSample(200, 100)
                .AddFlowSample(250, 125)
                .AddFlowSample(300, 160)
                .AddFlowSample(350, 175)
                .AddFlowSample(400, 180)
                .AddFlowSample(450, 190)
                .AddFlowSample(500, 200)
                .AddFlowSample(550, 205)
                .AddFlowSample(600, 210)
                .AddFlowSample(650, 210)
                .AddFlowSample(700, 210);

            return CylinderHeadNode.Standard(intakeCamshaft, exhaustCamshaft, intakeFlow, exhaustFlow,
                chamberVolume: 118.0 * U.cc,
                intakeRunnerVolume: 189.0 * U.cc,
                intakeRunnerCrossSectionArea: 37.8 * U.cm2,
                flipDisplay: flipDisplay);
        }

        /// <summary>An aftermarket big-block V8 head with large rectangular intake ports.</summary>
        public static CylinderHeadNode BigBlockRectanglePortHead(CamshaftNode intakeCamshaft, CamshaftNode exhaustCamshaft, bool flipDisplay = false)
        {
            FunctionNode intakeFlow = new FunctionNode(50 * U.thou)
                .AddFlowSample(0, 0)
                .AddFlowSample(50, 25)
                .AddFlowSample(100, 76)
                .AddFlowSample(150, 100)
                .AddFlowSample(200, 146)
                .AddFlowSample(250, 175)
                .AddFlowSample(300, 212)
                .AddFlowSample(350, 230)
                .AddFlowSample(400, 255)
                .AddFlowSample(450, 275)
                .AddFlowSample(500, 294)
                .AddFlowSample(550, 300)
                .AddFlowSample(600, 314)
                .AddFlowSample(650, 314)
                .AddFlowSample(700, 314);
            FunctionNode exhaustFlow = new FunctionNode(50 * U.thou)
                .AddFlowSample(0, 0)
                .AddFlowSample(50, 25)
                .AddFlowSample(100, 70)
                .AddFlowSample(150, 100)
                .AddFlowSample(200, 132)
                .AddFlowSample(250, 140)
                .AddFlowSample(300, 156)
                .AddFlowSample(350, 170)
                .AddFlowSample(400, 181)
                .AddFlowSample(450, 191)
                .AddFlowSample(500, 207)
                .AddFlowSample(550, 214)
                .AddFlowSample(600, 228)
                .AddFlowSample(650, 228)
                .AddFlowSample(700, 228);

            return CylinderHeadNode.Standard(intakeCamshaft, exhaustCamshaft, intakeFlow, exhaustFlow,
                chamberVolume: 118.0 * U.cc,
                intakeRunnerVolume: 315.0 * U.cc,
                intakeRunnerCrossSectionArea: 78.75 * U.cm2,
                flipDisplay: flipDisplay);
        }

        /// <summary>A small engine's head: the oval-port flow, scaled in lift and flow.</summary>
        public static CylinderHeadNode SmallEngineHead(CamshaftNode intakeCamshaft, CamshaftNode exhaustCamshaft, double chamberVolume = 100.0 * U.cc,
            double intakeRunnerVolume = 100.0 * U.cc, double intakeRunnerCrossSectionArea = 30.0 * U.cm2, double exhaustRunnerVolume = 100.0 * U.cc,
            double exhaustRunnerCrossSectionArea = 30.0 * U.cm2, double flowAttenuation = 1.0, double liftScale = 1.0, bool flipDisplay = false)
        {
            FunctionNode intakeFlow = new FunctionNode(50 * U.thou)
                .AddFlowSample(0 * liftScale, 0 * flowAttenuation)
                .AddFlowSample(50 * liftScale, 25 * flowAttenuation)
                .AddFlowSample(100 * liftScale, 75 * flowAttenuation)
                .AddFlowSample(150 * liftScale, 100 * flowAttenuation)
                .AddFlowSample(200 * liftScale, 130 * flowAttenuation)
                .AddFlowSample(250 * liftScale, 180 * flowAttenuation)
                .AddFlowSample(300 * liftScale, 190 * flowAttenuation)
                .AddFlowSample(350 * liftScale, 220 * flowAttenuation)
                .AddFlowSample(400 * liftScale, 240 * flowAttenuation)
                .AddFlowSample(450 * liftScale, 250 * flowAttenuation)
                .AddFlowSample(500 * liftScale, 260 * flowAttenuation)
                .AddFlowSample(550 * liftScale, 260 * flowAttenuation)
                .AddFlowSample(600 * liftScale, 260 * flowAttenuation)
                .AddFlowSample(650 * liftScale, 255 * flowAttenuation)
                .AddFlowSample(700 * liftScale, 250 * flowAttenuation);
            FunctionNode exhaustFlow = new FunctionNode(50 * U.thou)
                .AddFlowSample(0 * liftScale, 0 * flowAttenuation)
                .AddFlowSample(50 * liftScale, 25 * flowAttenuation)
                .AddFlowSample(100 * liftScale, 50 * flowAttenuation)
                .AddFlowSample(150 * liftScale, 75 * flowAttenuation)
                .AddFlowSample(200 * liftScale, 100 * flowAttenuation)
                .AddFlowSample(250 * liftScale, 125 * flowAttenuation)
                .AddFlowSample(300 * liftScale, 160 * flowAttenuation)
                .AddFlowSample(350 * liftScale, 175 * flowAttenuation)
                .AddFlowSample(400 * liftScale, 180 * flowAttenuation)
                .AddFlowSample(450 * liftScale, 190 * flowAttenuation)
                .AddFlowSample(500 * liftScale, 200 * flowAttenuation)
                .AddFlowSample(550 * liftScale, 205 * flowAttenuation)
                .AddFlowSample(600 * liftScale, 210 * flowAttenuation)
                .AddFlowSample(650 * liftScale, 210 * flowAttenuation)
                .AddFlowSample(700 * liftScale, 210 * flowAttenuation);

            return CylinderHeadNode.Standard(intakeCamshaft, exhaustCamshaft, intakeFlow, exhaustFlow,
                chamberVolume: chamberVolume,
                intakeRunnerVolume: intakeRunnerVolume,
                intakeRunnerCrossSectionArea: intakeRunnerCrossSectionArea,
                exhaustRunnerVolume: exhaustRunnerVolume,
                exhaustRunnerCrossSectionArea: exhaustRunnerCrossSectionArea,
                flipDisplay: flipDisplay);
        }

        // ---- Intakes ----

        /// <summary>A stock big-block V8 intake under a carburettor of <paramref name="carburetorCfm"/>.</summary>
        public static IntakeNode StockBigBlockIntake(double carburetorCfm = 650.0, double idleFlowRateCfm = 1.0, double idleThrottlePlatePosition = 0.975,
            double throttleGamma = 2.0) =>
            new IntakeNode(
                plenumVolume: 2.0 * U.L,
                plenumCrossSectionArea: 100.0 * U.cm2,
                intakeFlowRate: ScriptFunctions.K_carb(carburetorCfm),
                idleFlowRate: ScriptFunctions.K_carb(idleFlowRateCfm),
                idleThrottlePlatePosition: idleThrottlePlatePosition,
                throttleGamma: throttleGamma,
                runnerFlowRate: ScriptFunctions.K_carb(300.0),
                runnerLength: 6.0 * U.inch,
                velocityDecay: 1.0);

        /// <summary>A dual-plane, high-rise V8 intake under a carburettor of <paramref name="carburetorCfm"/>: freer runners.</summary>
        public static IntakeNode DualPlaneIntake(double carburetorCfm = 650.0, double idleFlowRateCfm = 1.0, double idleThrottlePlatePosition = 0.975,
            double throttleGamma = 2.0) =>
            new IntakeNode(
                plenumVolume: 2.0 * U.L,
                plenumCrossSectionArea: 100.0 * U.cm2,
                intakeFlowRate: ScriptFunctions.K_carb(carburetorCfm),
                idleFlowRate: ScriptFunctions.K_carb(idleFlowRateCfm),
                idleThrottlePlatePosition: idleThrottlePlatePosition,
                throttleGamma: throttleGamma,
                runnerFlowRate: ScriptFunctions.K_carb(500.0),
                runnerLength: 6.0 * U.inch,
                velocityDecay: 0.1);

        // ---- Distributors ----

        /// <summary>
        /// A cross-plane V8's distributor: firing order 1-8-4-3-6-5-7-2, an eighth of the cycle apart.
        /// <paramref name="wires"/> are the leads of cylinders 1 to 8.
        /// </summary>
        public static IgnitionModuleNode CrossPlaneV8Distributor(IgnitionWireNode[] wires, FunctionNode timingCurve, double revLimit = 5500 * U.rpm) =>
            new IgnitionModuleNode(timingCurve, revLimit: revLimit)
                .ConnectWire(wires[0], (0.0 / 8.0) * Cycle)
                .ConnectWire(wires[7], (1.0 / 8.0) * Cycle)
                .ConnectWire(wires[3], (2.0 / 8.0) * Cycle)
                .ConnectWire(wires[2], (3.0 / 8.0) * Cycle)
                .ConnectWire(wires[5], (4.0 / 8.0) * Cycle)
                .ConnectWire(wires[4], (5.0 / 8.0) * Cycle)
                .ConnectWire(wires[6], (6.0 / 8.0) * Cycle)
                .ConnectWire(wires[1], (7.0 / 8.0) * Cycle);

        /// <summary>A 90° V-twin's distributor: the second cylinder 3/8 of the cycle (270°) after the first.</summary>
        public static IgnitionModuleNode VTwin90Distributor(IgnitionWireNode[] wires, FunctionNode timingCurve, double revLimit = 5500 * U.rpm) =>
            new IgnitionModuleNode(timingCurve, revLimit: revLimit)
                .ConnectWire(wires[0], (0.0 / 8.0) * Cycle)
                .ConnectWire(wires[1], (3.0 / 8.0) * Cycle);

        /// <summary>A single cylinder's ignition.</summary>
        public static IgnitionModuleNode SingleCylinderDistributor(IgnitionWireNode[] wires, FunctionNode timingCurve, double revLimit = 5500 * U.rpm) =>
            new IgnitionModuleNode(timingCurve, revLimit: revLimit)
                .ConnectWire(wires[0], (0.0 / 8.0) * Cycle);
    }
}
