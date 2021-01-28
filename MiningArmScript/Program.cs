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

        enum MiningArmState
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

        List<IMyPistonBase> pistons = new List<IMyPistonBase>();
        IMyMotorStator drillRotor = null;
        List<IMyShipDrill> drills = new List<IMyShipDrill>();
        List<IMyMotorStator> hinges = new List<IMyMotorStator>();
        IMyLandingGear landingGearDrillHead = null;
        IMyLandingGear landingGearMiningArmPark = null;

        MyCommandLine _commandLine = new MyCommandLine();
        MiningArmTargetState targetState = MiningArmTargetState.UNDEFINED;
        MiningArmState currentState = MiningArmState.UNKNOWN;

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

            Runtime.UpdateFrequency = UpdateFrequency.Update10;

            var prefix = "BMT_";
            var miningArmGroup = GridTerminalSystem.GetBlockGroupWithName(prefix + "MiningArmGroup");
            if(miningArmGroup != null)
            {
                // get Pistons
                miningArmGroup.GetBlocksOfType(pistons);

                // get DrillHead Rotor and Arm Hinges
                var rotorsAndHinges = new List<IMyMotorStator>();
                miningArmGroup.GetBlocksOfType(rotorsAndHinges);
                foreach (var rotor in rotorsAndHinges)
                {
                    if (rotor.CustomName.Contains("[drills]"))
                    {
                        drillRotor = rotor;
                    }
                    else if (rotor.CubeGrid.Equals(Me.CubeGrid))
                    {
                        hinges.Add(rotor);
                    }
                    //Echo($"Rotor (Name): {rotor.Name}");
                    //Echo($"Rotor (DisplayName): {rotor.DisplayName}");
                    //Echo($"Rotor (DisplayNameText): {rotor.DisplayNameText}");
                    //Echo($"Rotor (CustomName): {rotor.CustomName}");
                    //Echo("");
                }

                // get Drills
                miningArmGroup.GetBlocksOfType(drills);

                // get LandingGears
                var landingGears = new List<IMyLandingGear>();
                miningArmGroup.GetBlocksOfType(landingGears);
                foreach (var landingGear in landingGears)
                {
                    if (landingGear.CustomName.Contains("[drills]"))
                    {
                        landingGearDrillHead = landingGear;
                    }
                    else if (landingGear.CustomName.Contains("[arm]"))
                    {
                        landingGearMiningArmPark = landingGear;
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
                    foreach (var hinge in hinges)
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
            Echo("drillRotorOrientation: " + drillRotor.Orientation);
            Echo("ArmGridPosition: " + hinges[0].TopGrid.GetPosition().ToString());
            Echo("Hinge Angle: " + hinges[0].Angle);


            


            Echo("");
            Echo("Executed instructions: " + Runtime.CurrentInstructionCount + "/" + Runtime.MaxInstructionCount);
        }

        private MiningArmState DetectCurrentState()
        {
            var refHinge = hinges[0];
            var hingeMinLimit = Math.Min(Math.Abs(refHinge.LowerLimitRad), Math.Abs(refHinge.UpperLimitRad));
            var hingeMaxLimit = Math.Max(Math.Abs(refHinge.LowerLimitRad), Math.Abs(refHinge.UpperLimitRad));
           
            if (Math.Abs(refHinge.Angle - hingeMinLimit) < 0.01)
            {
                return MiningArmState.DEPLOYED;
            } 
            else if ((Math.Abs(refHinge.Angle - hingeMaxLimit) < 0.01))
            {
                return MiningArmState.PARKED;
            }
            
            return MiningArmState.UNKNOWN;
        }
    }
}
