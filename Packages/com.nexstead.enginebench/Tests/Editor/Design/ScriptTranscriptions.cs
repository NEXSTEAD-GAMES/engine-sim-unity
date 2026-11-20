// Engine scripts written with the script nodes, to check the nodes build what engine-sim builds.
// MIT licence: LICENSE.md; engine-sim's notice: Third Party Notices.md.
using Nexstead.EngineBench.Design;
using Nexstead.EngineBench.Simulation;
using U = Nexstead.EngineBench.Design.ScriptUnits;

namespace Nexstead.EngineBench.Tests
{
    /// <summary>
    /// engine-sim's scripts for some presets, transcribed line by line with the script nodes. Each builds its preset's
    /// engine, vehicle and transmission;
    /// the engine carries the preset's name.
    /// </summary>
    public static class ScriptTranscriptions
    {
        const double Cycle = 2 * 360 * U.deg;

        /// <summary>The <c>boxer-4-rally-wagon</c> preset: a flat-4 with two camshafts a bank, firing 1-3-2-4.</summary>
        public static EngineDescription Flat4(string name)
        {
            CylinderHeadNode Head(CamshaftNode intakeCamshaft, CamshaftNode exhaustCamshaft, bool flipDisplay = false)
            {
                const double flowAttenuation = 1.0, liftScale = 1.0;
                FunctionNode intakeFlow = new FunctionNode(50 * U.thou)
                    .AddFlowSample(0 * liftScale, 0 * flowAttenuation)
                    .AddFlowSample(50 * liftScale, 58 * flowAttenuation)
                    .AddFlowSample(100 * liftScale, 103 * flowAttenuation)
                    .AddFlowSample(150 * liftScale, 156 * flowAttenuation)
                    .AddFlowSample(200 * liftScale, 214 * flowAttenuation)
                    .AddFlowSample(250 * liftScale, 249 * flowAttenuation)
                    .AddFlowSample(300 * liftScale, 268 * flowAttenuation)
                    .AddFlowSample(350 * liftScale, 280 * flowAttenuation)
                    .AddFlowSample(400 * liftScale, 280 * flowAttenuation)
                    .AddFlowSample(450 * liftScale, 281 * flowAttenuation);
                FunctionNode exhaustFlow = new FunctionNode(50 * U.thou)
                    .AddFlowSample(0 * liftScale, 0 * flowAttenuation)
                    .AddFlowSample(50 * liftScale, 37 * flowAttenuation)
                    .AddFlowSample(100 * liftScale, 72 * flowAttenuation)
                    .AddFlowSample(150 * liftScale, 113 * flowAttenuation)
                    .AddFlowSample(200 * liftScale, 160 * flowAttenuation)
                    .AddFlowSample(250 * liftScale, 196 * flowAttenuation)
                    .AddFlowSample(300 * liftScale, 222 * flowAttenuation)
                    .AddFlowSample(350 * liftScale, 235 * flowAttenuation)
                    .AddFlowSample(400 * liftScale, 245 * flowAttenuation)
                    .AddFlowSample(450 * liftScale, 246 * flowAttenuation);

                return new CylinderHeadNode(new StandardValvetrainNode(intakeCamshaft, exhaustCamshaft), intakeFlow, exhaustFlow,
                    chamberVolume: 67 * U.cc,
                    intakeRunnerVolume: 149.6 * U.cc,
                    intakeRunnerCrossSectionArea: 1.35 * U.inch * 1.35 * U.inch,
                    exhaustRunnerVolume: 50.0 * U.cc,
                    exhaustRunnerCrossSectionArea: 1.25 * U.inch * 1.25 * U.inch,
                    flipDisplay: flipDisplay);
            }

            var engine = new EngineNode(
                name: name,
                starterTorque: 70 * U.lb_ft,
                starterSpeed: 500 * U.rpm,
                redline: 6500 * U.rpm,
                fuel: new FuelNode(maxTurbulenceEffect: 2.5, maxBurningEfficiency: 0.75),
                throttleGamma: 2.0,
                hfGain: 0.01,
                noise: 1.0,
                jitter: 0.5,
                simulationFrequency: 20000);

            IgnitionWireNode wire1 = new IgnitionWireNode(), wire2 = new IgnitionWireNode(), wire3 = new IgnitionWireNode(), wire4 = new IgnitionWireNode();

            const double stroke = 79 * U.mm;
            const double bore = 99.5 * U.mm;
            const double rodLength = 5.142 * U.inch;
            const double rodMass = 535 * U.g;
            const double compressionHeight = 1.0 * U.inch;
            const double crankMass = 9.39 * U.kg;
            const double flywheelMass = 6.8 * U.kg;
            const double flywheelRadius = 6 * U.inch;

            double crankMoment = ScriptFunctions.DiskMomentOfInertia(crankMass, stroke / 2);
            double flywheelMoment = ScriptFunctions.DiskMomentOfInertia(flywheelMass, flywheelRadius);
            double otherMoment = ScriptFunctions.DiskMomentOfInertia(10 * U.kg, 6.0 * U.cm);

            var c0 = new CrankshaftNode(
                @throw: stroke / 2,
                flywheelMass: flywheelMass,
                mass: crankMass,
                frictionTorque: 1.0 * U.lb_ft,
                momentOfInertia: crankMoment + flywheelMoment + otherMoment,
                positionX: 0.0,
                positionY: 0.0,
                tdc: 180 * U.deg);

            RodJournalNode rj0 = new RodJournalNode(0.0 * U.deg), rj1 = new RodJournalNode(180.0 * U.deg),
                rj2 = new RodJournalNode(0.0 * U.deg), rj3 = new RodJournalNode(180.0 * U.deg);
            c0.AddRodJournal(rj0).AddRodJournal(rj1).AddRodJournal(rj2).AddRodJournal(rj3);

            var pistonParams = new PistonParameters(mass: (414 + 152) * U.g, compressionHeight: compressionHeight, wristPinPosition: 0.0,
                displacement: 0.0);
            var rodParams = new ConnectingRodParameters(mass: rodMass, momentOfInertia: ScriptFunctions.RodMomentOfInertia(rodMass, rodLength),
                centerOfMass: 0.0, length: rodLength);

            var intake = new IntakeNode(
                plenumVolume: 1.325 * U.L,
                plenumCrossSectionArea: 20.0 * U.cm2,
                intakeFlowRate: ScriptFunctions.K_carb(800.0),
                runnerFlowRate: ScriptFunctions.K_carb(250.0),
                runnerLength: 12.0 * U.inch,
                idleFlowRate: ScriptFunctions.K_carb(0.0),
                idleThrottlePlatePosition: 0.9985,
                velocityDecay: 0.5);

            var exhaustParams = new ExhaustSystemParameters(
                outletFlowRate: ScriptFunctions.K_carb(1000.0),
                primaryTubeLength: 10.0 * U.inch,
                primaryFlowRate: ScriptFunctions.K_carb(200.0),
                velocityDecay: 1.0,
                volume: 100.0 * U.L);
            var exhaust0 = new ExhaustSystemNode(exhaustParams, ImpulseResponseLibrary.MildExhaust0, audioVolume: 0.5 * 8);
            var exhaust1 = new ExhaustSystemNode(exhaustParams, ImpulseResponseLibrary.MildExhaust0, audioVolume: 1.0 * 8);

            var bankParams = new CylinderBankParameters(bore: bore, deckHeight: stroke / 2 + rodLength + compressionHeight);
            var b0 = new CylinderBankNode(bankParams, angle: 90.0 * U.deg);
            var b1 = new CylinderBankNode(bankParams, angle: -90.0 * U.deg);

            FunctionNode intakeLobe = ScriptFunctions.HarmonicCamLobe(durationAt50Thou: 232 * U.deg, gamma: 2.0, lift: 9.78 * U.mm, steps: 100);
            FunctionNode exhaustLobe = ScriptFunctions.HarmonicCamLobe(durationAt50Thou: 236 * U.deg, gamma: 2.0, lift: 9.60 * U.mm, steps: 100);

            // The script's camshaft part: a camshaft pair a bank, lobes a quarter cycle apart.
            var camParams = new CamshaftParameters(advance: 0 * U.deg, baseRadius: (34.0 / 2) * U.mm);
            const double intakeLobeCenter = 117 * U.deg, exhaustLobeCenter = 112 * U.deg;
            const double rot360 = 360 * U.deg;
            var intakeCam0 = new CamshaftNode(camParams, lobeProfile: intakeLobe);
            var exhaustCam0 = new CamshaftNode(camParams, lobeProfile: exhaustLobe);
            var intakeCam1 = new CamshaftNode(camParams, lobeProfile: intakeLobe);
            var exhaustCam1 = new CamshaftNode(camParams, lobeProfile: exhaustLobe);
            exhaustCam0
                .AddLobe(rot360 - exhaustLobeCenter + (0.0 / 4) * Cycle)
                .AddLobe(rot360 - exhaustLobeCenter + (1.0 / 4) * Cycle);
            intakeCam0
                .AddLobe(rot360 + intakeLobeCenter + (0.0 / 4) * Cycle)
                .AddLobe(rot360 + intakeLobeCenter + (1.0 / 4) * Cycle);
            exhaustCam1
                .AddLobe(rot360 - exhaustLobeCenter + (2.0 / 4) * Cycle)
                .AddLobe(rot360 - exhaustLobeCenter + (3.0 / 4) * Cycle);
            intakeCam1
                .AddLobe(rot360 + intakeLobeCenter + (2.0 / 4) * Cycle)
                .AddLobe(rot360 + intakeLobeCenter + (3.0 / 4) * Cycle);

            b0
                .AddCylinder(new PistonNode(pistonParams, blowby: ScriptFunctions.K_28inH2O(0.001)), new ConnectingRodNode(rodParams), rj0, intake,
                    exhaust0, wire1)
                .AddCylinder(new PistonNode(pistonParams, blowby: ScriptFunctions.K_28inH2O(0.002)), new ConnectingRodNode(rodParams), rj3, intake,
                    exhaust0, wire3)
                .SetCylinderHead(Head(intakeCam0, exhaustCam0, flipDisplay: true));
            b1
                .AddCylinder(new PistonNode(pistonParams, blowby: ScriptFunctions.K_28inH2O(0.001)), new ConnectingRodNode(rodParams), rj1, intake,
                    exhaust1, wire2)
                .AddCylinder(new PistonNode(pistonParams, blowby: ScriptFunctions.K_28inH2O(0.002)), new ConnectingRodNode(rodParams), rj2, intake,
                    exhaust1, wire4)
                .SetCylinderHead(Head(intakeCam1, exhaustCam1));

            engine.AddCylinderBank(b0).AddCylinderBank(b1);
            engine.AddCrankshaft(c0);

            FunctionNode timingCurve = new FunctionNode(1000 * U.rpm)
                .AddSample(0000 * U.rpm, 25 * U.deg)
                .AddSample(1000 * U.rpm, 25 * U.deg)
                .AddSample(2000 * U.rpm, 30 * U.deg)
                .AddSample(3000 * U.rpm, 40 * U.deg)
                .AddSample(4000 * U.rpm, 40 * U.deg);
            engine.AddIgnitionModule(new IgnitionModuleNode(timingCurve, revLimit: 6500 * U.rpm, limiterDuration: 0.08)
                .ConnectWire(wire1, (0.0 / 4.0) * Cycle)
                .ConnectWire(wire3, (1.0 / 4.0) * Cycle)
                .ConnectWire(wire2, (2.0 / 4.0) * Cycle)
                .ConnectWire(wire4, (3.0 / 4.0) * Cycle));

            var vehicle = new VehicleNode(
                mass: 2700 * U.lb,
                dragCoefficient: 0.2,
                crossSectionalArea: (66 * U.inch) * (56 * U.inch),
                diffRatio: 3.9,
                tireRadius: 10 * U.inch,
                rollingResistance: 300 * U.N);
            TransmissionNode transmission = new TransmissionNode(maxClutchTorque: 300 * U.lb_ft)
                .AddGear(3.636)
                .AddGear(2.375)
                .AddGear(1.761)
                .AddGear(1.346)
                .AddGear(0.971)
                .AddGear(0.756);

            return engine.Describe(vehicle, transmission);
        }

        /// <summary>
        /// The <c>v8-big-block-truck</c> preset: a cross-plane big-block V8 from library parts (stock camshaft, oval-port
        /// heads, stock intake, V8 distributor), with engine-sim's default vehicle and gearbox.
        /// </summary>
        public static EngineDescription BigBlockTruckV8(string name)
        {
            var wires = new IgnitionWireNode[8];
            for (int i = 0; i < wires.Length; ++i) wires[i] = new IgnitionWireNode();

            var engine = new EngineNode(
                name: name,
                starterTorque: 200 * U.lb_ft,
                redline: 5500 * U.rpm,
                fuel: new FuelNode(maxTurbulenceEffect: 3.0, burningEfficiencyRandomness: 0.5, maxBurningEfficiency: 0.85),
                throttleGamma: 1.5);

            var c0 = new CrankshaftNode(
                @throw: 2.0 * U.inch,
                flywheelMass: 29 * 2 * U.lb,
                mass: 75 * U.lb,
                frictionTorque: 10.0 * U.lb_ft,
                momentOfInertia: 0.22986844776863666 * 2,
                positionX: 0.0,
                positionY: 0.0,
                tdc: U.pi / 4);

            RodJournalNode rj0 = new RodJournalNode(0.0), rj1 = new RodJournalNode(-U.pi / 2), rj2 = new RodJournalNode(-3.0 * U.pi / 2),
                rj3 = new RodJournalNode(U.pi);
            c0.AddRodJournal(rj0).AddRodJournal(rj1).AddRodJournal(rj2).AddRodJournal(rj3);

            var pistonParams = new PistonParameters(mass: 880 * U.g, blowby: 0, compressionHeight: 1.640 * U.inch, wristPinPosition: 0.0,
                displacement: 0.0);
            var rodParams = new ConnectingRodParameters(mass: 785.0 * U.g, momentOfInertia: 0.0015884918028487504, centerOfMass: 0.0,
                length: 6.135 * U.inch);
            var bankParams = new CylinderBankParameters(bore: 4.25 * U.inch, deckHeight: 9.8 * U.inch);
            IntakeNode intake = PartLibrary.StockBigBlockIntake(carburetorCfm: 650.0, idleFlowRateCfm: 0.007, idleThrottlePlatePosition: 0.991);

            var exhaustParams = new ExhaustSystemParameters(
                outletFlowRate: ScriptFunctions.K_carb(550.0),
                primaryTubeLength: 10.0 * U.inch,
                primaryFlowRate: ScriptFunctions.K_carb(100.0),
                velocityDecay: 1.0,
                length: 100.0 * U.inch);
            const double distance = 5.0 * U.inch;
            var exhaust0 = new ExhaustSystemNode(exhaustParams, ImpulseResponseLibrary.Default, length: (180 + 72.0) * U.inch, audioVolume: 5.5);
            var exhaust1 = new ExhaustSystemNode(exhaustParams, ImpulseResponseLibrary.Default, length: 180.0 * U.inch, audioVolume: 5.5);

            PistonNode Piston(double blowby) => new PistonNode(pistonParams, blowby: ScriptFunctions.K_28inH2O(blowby));

            var b0 = new CylinderBankNode(bankParams, angle: -45 * U.deg);
            b0
                .AddCylinder(Piston(0.2), new ConnectingRodNode(rodParams), rj0, intake, exhaust0, wires[0], primaryLength: distance * 4, soundAttenuation: 0.9)
                .AddCylinder(Piston(0.6), new ConnectingRodNode(rodParams), rj1, intake, exhaust0, wires[2], primaryLength: distance * 3, soundAttenuation: 1.1)
                .AddCylinder(Piston(0.6), new ConnectingRodNode(rodParams), rj2, intake, exhaust0, wires[4], primaryLength: distance * 2, soundAttenuation: 0.8)
                .AddCylinder(Piston(0.4), new ConnectingRodNode(rodParams), rj3, intake, exhaust0, wires[6], primaryLength: distance * 1,
                    soundAttenuation: 0.85);

            var b1 = new CylinderBankNode(bankParams, angle: 45.0 * U.deg);
            b1
                .AddCylinder(Piston(0.2), new ConnectingRodNode(rodParams), rj0, intake, exhaust1, wires[1], primaryLength: distance * 4, soundAttenuation: 0.9)
                .AddCylinder(Piston(0.2), new ConnectingRodNode(rodParams), rj1, intake, exhaust1, wires[3], primaryLength: distance * 3, soundAttenuation: 1.1)
                .AddCylinder(Piston(0.6), new ConnectingRodNode(rodParams), rj2, intake, exhaust1, wires[5], primaryLength: distance * 2, soundAttenuation: 0.8)
                .AddCylinder(Piston(0.6), new ConnectingRodNode(rodParams), rj3, intake, exhaust1, wires[7], primaryLength: distance * 1,
                    soundAttenuation: 0.75);

            engine.AddCylinderBank(b0).AddCylinderBank(b1);
            engine.AddCrankshaft(c0);

            CamshaftSet camshaft = PartLibrary.StockBigBlockCamshaft();
            b0.SetCylinderHead(PartLibrary.BigBlockOvalPortHead(camshaft.intakeCam0, camshaft.exhaustCam0));
            b1.SetCylinderHead(PartLibrary.BigBlockOvalPortHead(camshaft.intakeCam1, camshaft.exhaustCam1, flipDisplay: true));

            FunctionNode timingCurve = new FunctionNode(1000 * U.rpm)
                .AddSample(0000 * U.rpm, 12 * U.deg)
                .AddSample(1000 * U.rpm, 12 * U.deg)
                .AddSample(2000 * U.rpm, 20 * U.deg)
                .AddSample(3000 * U.rpm, 30 * U.deg)
                .AddSample(4000 * U.rpm, 38 * U.deg)
                .AddSample(5000 * U.rpm, 38 * U.deg)
                .AddSample(6000 * U.rpm, 38 * U.deg);
            engine.AddIgnitionModule(PartLibrary.CrossPlaneV8Distributor(wires, timingCurve, revLimit: 7000 * U.rpm));

            return engine.Describe();
        }
    }
}
