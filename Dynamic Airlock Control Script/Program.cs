using Sandbox.Game;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.Entities.Blocks;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
    public partial class Program : MyGridProgram
    {
        float Padding = 12f;

        bool InitialSetupComplete = false;
        bool DoorsLocked = false;
        bool GateMode = false;
        bool InteriorMode = false;
        bool AtmosphereCheck = false;
        bool OxygenTankAttached = false;
        bool OxygenTankFull = false;
        double OxygenTankFillPercentage = 0.0;

        float TextHeight;
        string AirlockStatus = "";
        string GatePhase = "";

        // Identifying Text For Hardware
        string HardwareIdentifier;

        //Script Blocks
        IMyAirVent AirlockAirVent; //Necessary Block
        IMyGasTank PrimaryOxygenTank;
        IMyAirVent ExternalAirVent;
        IMyAirVent GateAirVent;
        IMyProgrammableBlock ProgrammableBlock;
        
        //Block Lists
        List<IMyDoor> ExteriorDoorGroup = new List<IMyDoor>();
        List<IMyDoor> InteriorDoorGroup = new List<IMyDoor>();
        List<IMyDoor> AllAirlockDoors = new List<IMyDoor>();
        List<IMyDoor> GateGroup = new List<IMyDoor>();
        List<IMyInteriorLight> AirlockStatusLightGroup = new List<IMyInteriorLight>();
        List<IMyTextPanel> AirlockDisplays = new List<IMyTextPanel>();

        //Status Variables: 0 = Pressurizing, 1 = Pressurized, 2 = Depressurizing, 3 = Depressurized, 4 = Working
        int LightStatusNumber = 4;
        int AirlockStatusNumber = 4;

        //Light Data
        Color Red = new Color(255, 0, 0); //Depressurizing
        Color Orange = new Color(255, 125, 0); //Working
        Color Green = new Color(0, 255, 0); //Pressurizing
        float[] AirlockLightBlinkIntervals = {1f, 0f, 1f, 0f, 0f};
        float[] AirlockLightsBlinkLengths = {50f, 0f, 50f, 0f, 0f};
        float[] AirlockLightsBlinkOffsets = {0f, 0f, 0f, 0f, 0f};



        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            ProgrammableBlock = Me;
        }

        public void Save()
        {
            
        }

        public void Main(string argument, UpdateType UpdateSource)
        {
            //First check if there is an LCD to display information, otherwise use Echo for output.
            

            // Ensure necessary blocks are present.
            if (!InitialSetupComplete)
            {
                InitialSetupComplete = InitialHardwareSetup();
                return;
            }

            //CheckForAtmosphere();
            UpdateAirlockInformation();
            ProcessArguments(argument);
            AirlockCycling();
            AirlockLightManager();


        }//Ends Main

        public void ProcessArguments(string argument)
        {
            argument = argument.ToLower();
            
            if (argument == "cycle")
            {
                //If Pressurized, start depressurization
                if (AirlockStatusNumber == 1)
                {
                    AirlockStatus = "Depressurizing";
                    AirlockStatusNumber = 2;
                }
                //If Depressurized, start pressurization
                else if (AirlockStatusNumber == 3)
                {
                    AirlockStatus = "Pressurizing";
                    AirlockStatusNumber = 0;
                }

                LightStatusNumber = 4;
            }

            if (argument == "update")
            {
                AdditionalHardwareCheck();
            }
        }//Ends ProcessArguments

        public void CheckOxygenTankFillLevel() 
        {
            //Retrieve fill ration (0.0 to 1.0), then convert to perxcentage for comparison
            double OxygenTankFillRatio = PrimaryOxygenTank.FilledRatio;
            OxygenTankFillPercentage = OxygenTankFillRatio * 100;

            OxygenTankFull = (OxygenTankFillPercentage >= 98);
        }//Ends CheckOxygenTankLevel

        public void UpdateAirlockInformation() 
        {
            //Oxygen tank information
            if (OxygenTankAttached)
            {
                //Check if Oxygen tank is full
                CheckOxygenTankFillLevel();
            }
        }//Ends UpdateAirlockInformation

        public void AirlockLightManager()
        {
            if (AirlockStatusLightGroup.Count == 0){
                return;
            }
            
            Color[] LightColors = {Orange, Green, Red, Red, Orange};
            
            //Use status number to retrieve light data
            Color CurrentLightColor = LightColors[LightStatusNumber];
            float CurrentBlinkTime = AirlockLightBlinkIntervals[LightStatusNumber];
            float CurrentBlinkLength = AirlockLightsBlinkLengths[LightStatusNumber];
            float CurrentBlinkOffset = AirlockLightsBlinkOffsets[LightStatusNumber];

            //Set light data for each light
            foreach (IMyInteriorLight Light in AirlockStatusLightGroup)
            {
                Light.Color = CurrentLightColor;
                Light.BlinkIntervalSeconds = CurrentBlinkTime;
                Light.BlinkLength = CurrentBlinkLength;
                Light.BlinkOffset = CurrentBlinkOffset;
            }
        }//Ends AirlockLightManager

        public void AdditionalHardwareCheck()
        {
            AirlockStatusLightGroup.Clear();
            AirlockDisplays.Clear();
            PrimaryOxygenTank = null;

            //Check for primary oxygen tank
            List<IMyGasTank> AllGasTanks = new List<IMyGasTank>();
            GridTerminalSystem.GetBlocksOfType(AllGasTanks);
            string TankIdentifier = "Primary Oxygen Tank";
            foreach (IMyGasTank Tank in AllGasTanks)
            {
                string TankName = Tank.CustomName.ToLower();
                if (TankName.Contains(TankIdentifier.ToLower()))
                {
                    PrimaryOxygenTank = Tank;
                    break;
                }
            }

            //Check for Display
            List<IMyTextPanel> AllDisplays = new List<IMyTextPanel>();
            GridTerminalSystem.GetBlocksOfType(AllDisplays);
            string DisplayIdentifier = HardwareIdentifier + " Display";
            foreach (IMyTextPanel Display in AllDisplays)
            {
                string DisplayName = Display.CustomName.ToLower();
                if (DisplayName.Contains(DisplayIdentifier.ToLower()))
                {
                    AirlockDisplays.Add(Display);
                }
            }

            //Check for status lights
            List<IMyInteriorLight> AllLights = new List<IMyInteriorLight>();
            GridTerminalSystem.GetBlocksOfType(AllLights);
            string LightIdentifier = HardwareIdentifier + " Status";
            foreach (IMyInteriorLight Light in AllLights)
            {
                string LightName = Light.CustomName.ToLower();
                if (LightName.Contains(LightIdentifier.ToLower()))
                {
                    AirlockStatusLightGroup.Add(Light);
                }
            }

            OxygenTankAttached = (PrimaryOxygenTank != null);
        }//Ends AdditionalHardwareCheck

        public void DrawUI(IMyTextPanel Display) 
        {
            var surface = Display;
            surface.ContentType = ContentType.SCRIPT;
            surface.Script = "";

            Vector2 TextureSize = surface.TextureSize;
            Vector2 CanvasSize = surface.SurfaceSize;
            Vector2 ViewPortOffset = (TextureSize - CanvasSize) / 2;

            using (var frame = surface.DrawFrame())
            {
                float FontScale = 1.2f;
                Vector2 textSize = surface.MeasureStringInPixels(
                new StringBuilder("TextHeightScaleText"),
                "White",   // font name
                FontScale   // font scale
                );

                TextHeight = textSize.Y;
                float Padding = 12f;
                float LineCenterX = ViewPortOffset.X + (CanvasSize.X / 2f);
                float BoxWidth = 5f;
                float StartY = ViewPortOffset.Y + Padding; // begin at padding
            }
        }//Ends DrawUI

        public void CheckForAtmosphere()
        {
            if (ExternalAirVent == null) { 
                return;
            }

            //0.2f to reduce false positives
            float ExteriorOxygenLevel = ExternalAirVent.GetOxygenLevel();
            AtmosphereCheck = (ExteriorOxygenLevel >= 0.20f) ? true : false;
        }//Ends CheckForAtmosphere

        public void AirlockCycling()
        {
            if (AirlockStatusNumber == 0) //Pressurizing
            {
                PressurizeAirlock();
            }
            else if (AirlockStatusNumber == 2) //Depressurizing
            {
                DepressurizeAirlock();
            }

        }//Ends AirlockCycling

        public void PressurizeAirlock()
        {
            //Set status outside of door logic, in case doors are already closed
            LightStatusNumber = 0; //Pressurizing

            if (!DoorsLocked)
            {
                //Close doors first, then check if they are closed
                //Use door #1 for comparison, as it must exist after initialization
                //Check exterior doors as the interior doors will be closed already
                if (ExteriorDoorGroup[0].Status == DoorStatus.Closed)
                {
                    foreach (IMyDoor Door in AllAirlockDoors)
                    {
                        Door.Enabled = false;
                    }

                    //Start Pressurization
                    DoorsLocked = true;
                    AirlockAirVent.Depressurize = false;
                }
                else
                {
                    //Close All Doors, check if they are closed, then lock.
                    foreach (IMyDoor Door in AllAirlockDoors)
                    {
                        Door.ApplyAction("Open_Off");
                    }
                }
                //Outside of door logic, in case doors are already closed
                
            }
            else
            {
                if (AirlockAirVent.GetOxygenLevel() >= 0.98)
                {
                    foreach (IMyDoor Door in InteriorDoorGroup)
                    {
                        Door.Enabled = true;
                        Door.ApplyAction("Open_On");
                    }

                    DoorsLocked = false;
                    AirlockStatusNumber = 1; //Pressurized
                    LightStatusNumber = 1; //Pressurized
                    AirlockStatus = "Pressurized";
                }
            }
        }//Ends PressurizeAirlock

        public void DepressurizeAirlock()
        {
            //Outside of door logic, in case doors are already closed
            LightStatusNumber = 2; //Depressurizing

            if (!DoorsLocked)
            {
                //Close doors first, then check if they are closed
                //Use door #1 for comparison, as it must exist after initialization
                //Check Interior doors as the Exterior doors will be closed already
                if (InteriorDoorGroup[0].Status == DoorStatus.Closed)
                {
                    foreach (IMyDoor door in AllAirlockDoors)
                    {
                        door.Enabled = false;
                    }
                    //Start Depressurization
                    DoorsLocked = true;
                    AirlockAirVent.Depressurize = true;
                }
                else
                {
                    //Close All Doors, check if they are closed, then lock.
                    foreach (IMyDoor door in AllAirlockDoors)
                    {
                        door.ApplyAction("Open_Off");
                    }
                }
            }
            else
            {
                if (AirlockAirVent.GetOxygenLevel() <= 0.01 || OxygenTankFull)
                {
                    foreach (IMyDoor Door in ExteriorDoorGroup)
                    {
                        Door.Enabled = true;
                        Door.ApplyAction("Open_On");
                    }

                    if (ExteriorDoorGroup[0].Status == DoorStatus.Open)
                    {
                        DoorsLocked = false;
                        AirlockStatusNumber = 3; //Depressurized
                        LightStatusNumber = 3; //Depressurized
                        AirlockStatus = "Depressurized";
                    }
                }
            }
        }//Ends DepressurizeAirlock

        public bool InitialHardwareSetup()
        {
            int InitializedBlockCount = 0;
            HardwareIdentifier = ProgrammableBlock.CustomData.Trim();
            if (string.IsNullOrWhiteSpace(HardwareIdentifier))
            {
                Echo("Provide Identifying Tag");
                return false;
            }

            string AirlockVentIdentifier = HardwareIdentifier + " Air Vent";
            string ExteriorDoorIdentifier = HardwareIdentifier + " Exterior";
            string InteriorDoorIdentifier = HardwareIdentifier + " Interior";

            List<IMyDoor> AllDoors = new List<IMyDoor>();
            GridTerminalSystem.GetBlocksOfType(AllDoors);

            //Check for exterior doors, and add door to list if name contains identifier
            if (ExteriorDoorGroup.Count == 0)
            {
                Echo("Provide Exterior Door(s)");
                foreach (IMyDoor Door in AllDoors)
                {
                    //Ensure both the name and identifier are lowercase for comparison
                    string DoorName = Door.CustomName.ToLower();
                    if (DoorName.Contains(ExteriorDoorIdentifier.ToLower()))
                    {
                        ExteriorDoorGroup.Add(Door);
                    }
                }
            }
            else {
                InitializedBlockCount++;
            }
            if (InteriorDoorGroup.Count == 0)
            {
                Echo("Provide Interior Door(s)");
                foreach (IMyDoor Door in AllDoors)
                {
                    string DoorName = Door.CustomName.ToLower();
                    if (DoorName.Contains(InteriorDoorIdentifier.ToLower()))
                    {
                        InteriorDoorGroup.Add(Door);
                    }
                }
            }
            else
            {
                InitializedBlockCount++;
            }

            //Check for necessary air vent
            if (AirlockAirVent == null)
            {
                Echo("Missing Airlock Air Vent");
                List<IMyAirVent> AllVents = new List<IMyAirVent>();
                GridTerminalSystem.GetBlocksOfType(AllVents);

                foreach (IMyAirVent Vent in AllVents)
                {
                    string VentName = Vent.CustomName.ToLower();
                    if (VentName.Contains(HardwareIdentifier.ToLower()))
                    {
                        AirlockAirVent = Vent;
                    }
                }
            }
            else
            {
                InitializedBlockCount++;
            }

            bool InitializationSuccess = (InitializedBlockCount == 3) ? true : false;
            //Initial Setup once necessary blocks are assigned
            if (InitializationSuccess)
            {
                AllAirlockDoors.AddRange(ExteriorDoorGroup);
                AllAirlockDoors.AddRange(InteriorDoorGroup);
                //Check for additional hardware one time after initialization
                AdditionalHardwareCheck();
                UpdateAirlockInformation();

                if (AirlockAirVent.GetOxygenLevel() >= 0.95)
                {
                    AirlockStatusNumber = 0; //Pressuring to begin
                    PressurizeAirlock();
                }
                else
                {
                    AirlockStatusNumber = 2; //Depressurized to begin
                    DepressurizeAirlock();
                }
                
                AirlockLightManager(); //Update Lights
            }
            return InitializationSuccess;
        }//Ends InitialBlockSetup
    }//Ends Class
}//Ends NameSpace
