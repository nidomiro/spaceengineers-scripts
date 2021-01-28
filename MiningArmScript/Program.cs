using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        // This file contains your actual script.
        //
        // You can either keep all your code here, or you can create separate
        // code files to make your program easier to navigate while coding.
        //
        // In order to add a new utility class, right-click on your project, 
        // select 'New' then 'Add Item...'. Now find the 'Space Engineers'
        // category under 'Visual C# Items' on the left hand side, and select
        // 'Utility Class' in the main area. Name it in the box below, and
        // press OK. This utility class will be merged in with your code when
        // deploying your final script.
        //
        // You can also simply create a new utility class manually, you don't
        // have to use the template if you don't want to. Just do so the first
        // time to see what a utility class looks like.
        // 
        // Go to:
        // https://github.com/malware-dev/MDK-SE/wiki/Quick-Introduction-to-Space-Engineers-Ingame-Scripts
        //
        // to learn more about ingame scripts.

        enum MiningArmTargetState {
            UNDEFINED,
            PARK,
            DEPLOYED,
            DRILLING
        }

        enum MiningArmStateEnum
        {
            UNKNOWN,
            PARKING,
            PARKED,
            DEPLOYING,
            DEPLOYED,
            DRILLING,
            DRILLED,
            RETRACTING
        }

        struct MiningArmState
        {
            public MiningArmStateEnum state;
            public float hingeAngle;

            public MiningArmState(MiningArmStateEnum state, float hingeAngle)
            {
                this.state = state;
                this.hingeAngle = hingeAngle ;
            }

            public MiningArmState(MiningArmState? value = null)
            {
                this.state = value?.state ?? MiningArmStateEnum.UNKNOWN;
                this.hingeAngle = value?.hingeAngle ?? float.NaN;
            }

            public override string ToString()
            {
                return $"MiningArmState {{ state: {state}, hingeAngle: {hingeAngle} }}";
            }

        }

        class MiningArmBlocks
        {
            public List<IMyPistonBase> pistons = new List<IMyPistonBase>();
            public IMyMotorStator drillRotor = null;
            public List<IMyShipDrill> drills = new List<IMyShipDrill>();
            public List<IMyMotorStator> hinges = new List<IMyMotorStator>();
            public IMyLandingGear landingGearDrillHead = null;
            public IMyLandingGear landingGearMiningArmPark = null;

            public IMyMotorStator referenceHinge
            {
                get
                {
                    return hinges[0];
                }
            }
        }

        MiningArmBlocks miningArmBlocks = new MiningArmBlocks();

       

        MyCommandLine _commandLine = new MyCommandLine();
        MiningArmTargetState targetState = MiningArmTargetState.UNDEFINED;
        MiningArmState previousState = new MiningArmState();
        MiningArmState currentState = new MiningArmState();

        public Program()
        {
            // The constructor, called only once every session and
            // always before any other method is called. Use it to
            // initialize your script. 
            //     
            // The constructor is optional and can be removed if not
            // needed.
            // 
            // It's recommended to set Runtime.UpdateFrequency 
            // here, which will allow your script to run itself without a 
            // timer block.

            Runtime.UpdateFrequency = UpdateFrequency.Update100;

            var prefix = "BMT_";
            var miningArmGroup = GridTerminalSystem.GetBlockGroupWithName(prefix + "MiningArmGroup");
            if(miningArmGroup != null)
            {
                // get Pistons
                miningArmGroup.GetBlocksOfType(miningArmBlocks.pistons);

                // get DrillHead Rotor and Arm Hinges
                var rotorsAndHinges = new List<IMyMotorStator>();
                miningArmGroup.GetBlocksOfType(rotorsAndHinges);
                foreach (var rotor in rotorsAndHinges)
                {
                    if (rotor.CustomName.Contains("[drills]"))
                    {
                        miningArmBlocks.drillRotor = rotor;
                    }
                    else if (rotor.CubeGrid.Equals(Me.CubeGrid))
                    {
                        miningArmBlocks.hinges.Add(rotor);
                    }
                    //Echo($"Rotor (Name): {rotor.Name}");
                    //Echo($"Rotor (DisplayName): {rotor.DisplayName}");
                    //Echo($"Rotor (DisplayNameText): {rotor.DisplayNameText}");
                    //Echo($"Rotor (CustomName): {rotor.CustomName}");
                    //Echo("");
                }

                // get Drills
                miningArmGroup.GetBlocksOfType(miningArmBlocks.drills);

                // get LandingGears
                var landingGears = new List<IMyLandingGear>();
                miningArmGroup.GetBlocksOfType(landingGears);
                foreach (var landingGear in landingGears)
                {
                    if (landingGear.CustomName.Contains("[drills]"))
                    {
                        miningArmBlocks.landingGearDrillHead = landingGear;
                    }
                    else if (landingGear.CustomName.Contains("[arm]"))
                    {
                        miningArmBlocks.landingGearMiningArmPark = landingGear;
                    }
                }

            }
            else
            {
                Echo($"Group '{prefix + "MiningArmGroup"}' not found.");
            }

            



        }

        public void Save()
        {
            // Called when the program needs to save its state. Use
            // this method to save your state to the Storage field
            // or some other means. 
            // 
            // This method is optional and can be removed if not
            // needed.
        }

        public void Main(string argument, UpdateType updateSource)
        {
            Echo($"Time since last execute: {Runtime.TimeSinceLastRun}");
            previousState = currentState;

            if (_commandLine.TryParse(argument))
            {
                var newTargetState = _commandLine.Argument(0);
                if (String.Equals(newTargetState, "park", StringComparison.OrdinalIgnoreCase))
                {
                    targetState = MiningArmTargetState.PARK;
                }
                else if (String.Equals(newTargetState, "deploy", StringComparison.OrdinalIgnoreCase))
                {
                    targetState = MiningArmTargetState.DEPLOYED;
                    foreach (var hinge in miningArmBlocks.hinges)
                    {
                        hinge.TargetVelocityRPM = -1 * hinge.TargetVelocityRPM;
                            }
                }
                else if (String.Equals(newTargetState, "drill", StringComparison.OrdinalIgnoreCase))
                {
                    targetState = MiningArmTargetState.DRILLING;
                }
                else
                {
                    Echo("Unknown Command: " + newTargetState);
                }
                
            }

            currentState = DetectCurrentState();


            Echo("TagetState:   " + targetState);
            Echo("CurrentState: " + currentState);
            Echo("drillRotorOrientation: " + miningArmBlocks.drillRotor.Orientation);
            Echo("ArmGridPosition: " + miningArmBlocks.referenceHinge.TopGrid.GetPosition().ToString());
            Echo("Hinge Angle: " + miningArmBlocks.referenceHinge.Angle);


            


            Echo("");
            Echo("Executed instructions: " + Runtime.CurrentInstructionCount + "/" + Runtime.MaxInstructionCount);
        }

        private MiningArmState DetectCurrentState()
        {
            return new MiningArmState(
                DetectCurrentStateEnum(),
                miningArmBlocks.referenceHinge.Angle
                );
        }

        private MiningArmStateEnum DetectCurrentStateEnum()
        {
            var refHinge = miningArmBlocks.referenceHinge;
            var hingeMinLimit = Math.Min(Math.Abs(refHinge.LowerLimitRad), Math.Abs(refHinge.UpperLimitRad));
            var hingeMaxLimit = Math.Max(Math.Abs(refHinge.LowerLimitRad), Math.Abs(refHinge.UpperLimitRad));
           
            if (Math.Abs(refHinge.Angle - hingeMinLimit) < 0.01)
            {
                return MiningArmStateEnum.DEPLOYED;
            } 
            else if ((Math.Abs(refHinge.Angle - hingeMaxLimit) < 0.01))
            {
                return MiningArmStateEnum.PARKED;
            }
            else if (Math.Abs(refHinge.Angle) >= (hingeMinLimit - 0.01)  && Math.Abs(refHinge.Angle) <= (hingeMaxLimit + 0.01)) 
            {
                var direction = miningArmBlocks.referenceHinge.Angle - previousState.hingeAngle;

                if (Math.Abs(refHinge.LowerLimitRad) > Math.Abs(refHinge.UpperLimitRad))
                {
                    direction *= -1;
                }
                

                return (direction > 0 )? MiningArmStateEnum.PARKING : MiningArmStateEnum.DEPLOYING; // TODO: Differentiate between PARKING and DEPLOYING
            }
            
            return MiningArmStateEnum.UNKNOWN;
        }
    }
}
