using Sandbox.Game;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.Entities.Blocks;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

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
using VRageRender;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        //One instance of this class in one airlock
        // All hardware lookups are scoped to blocks whose CustomName contains this tag.
        public class Airlock
        {
            //Reference back to the owning program for Echo
            Program Program;

            public string HardwareIdentifier;
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

            //Script Blocks
            IMyAirVent AirlockAirVent; //Necessary Block
            IMyGasTank PrimaryOxygenTank;
            IMyAirVent ExternalAirVent;
            IMyAirVent GateAirVent;

            //Block Lists
            List<IMyDoor> ExteriorDoorGroup = new List<IMyDoor>();
            List<IMyDoor> InteriorDoorGroup = new List<IMyDoor>();
            List<IMyDoor> AllAirlockDoors = new List<IMyDoor>();
            List<IMyDoor> GateGroup = new List<IMyDoor>();
            List<IMyInteriorLight> AirlockStatusLightGroup = new List<IMyInteriorLight>();
            List<IMyTextSurface> AirlockDisplays = new List<IMyTextSurface>();

            //Status Variables: 0 = Pressurizing, 1 = Pressurized, 2 = Depressurizing, 3 = Depressurized, 4 = Working
            int LightStatusNumber = 4;
            int AirlockStatusNumber = 4;

            //Light Data
            static readonly Color Red = new Color(255, 0, 0); //Depressurizing
            static readonly Color Orange = new Color(255, 125, 0); //Working
            static readonly Color Green = new Color(0, 255, 0); //Pressurizing
            static readonly Color CustomGrey = new Color(50, 50, 50); //Custom Grey
            Color CurrentLightColor;
            static readonly float[] AirlockLightBlinkIntervals = { 1f, 0f, 1f, 0f, 0f };
            static readonly float[] AirlockLightsBlinkLengths = { 50f, 0f, 50f, 0f, 0f };
            static readonly float[] AirlockLightsBlinkOffsets = { 0f, 0f, 0f, 0f, 0f };

            //Constructor
            public Airlock(Program program, string HardwareTag)
            {
                Program = program;
                HardwareIdentifier = HardwareTag;
            }

            public bool GetSetupCompletionStatus()
            {
                return InitialSetupComplete;
            }

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
                //Retrieve fill ration (0.0 to 1.0), then convert to percentage for comparison
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

                Color[] LightColors = { Orange, Green, Red, Red, Orange };

                //Use status number to retrieve light data
                CurrentLightColor = LightColors[LightStatusNumber];
                float CurrentBlinkTime = AirlockLightBlinkIntervals[LightStatusNumber];
                float CurrentBlinkLength = AirlockLightsBlinkLengths[LightStatusNumber];
                float CurrentBlinkOffset = AirlockLightsBlinkOffsets[LightStatusNumber];

                if (AirlockStatusLightGroup.Count == 0)
                {
                    return;
                }

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
                Program.GridTerminalSystem.GetBlocksOfType(AllGasTanks, Tank => Tank.CubeGrid == Program.Me.CubeGrid);
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

                //Check for seperate displays
                List<IMyTextPanel> AllDisplays = new List<IMyTextPanel>();
                Program.GridTerminalSystem.GetBlocksOfType(AllDisplays, Display => Display.CubeGrid == Program.Me.CubeGrid);
                string DisplayIdentifier = HardwareIdentifier + " Airlock Display";
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
                Program.GridTerminalSystem.GetBlocksOfType(AllLights, Light => Light.CubeGrid == Program.Me.CubeGrid);
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

                //Check for button panel screens
                List<IMyButtonPanel> AllButtonPanels = new List<IMyButtonPanel>();
                Program.GridTerminalSystem.GetBlocksOfType(AllButtonPanels, ButtonPanel => ButtonPanel.CubeGrid == Program.Me.CubeGrid);
                string ButtonPanelIdentifier = "Button Panel";
                foreach (IMyButtonPanel ButtonPanel in AllButtonPanels)
                {
                    string ButtonPanelName = ButtonPanel.CustomName.ToLower();
                    if (ButtonPanelName.Contains(HardwareIdentifier.ToLower()) && ButtonPanelName.Contains(ButtonPanelIdentifier.ToLower()))
                    {
                        IMyTextSurfaceProvider SurfaceProvider = ButtonPanel as IMyTextSurfaceProvider;
                        if (SurfaceProvider != null && SurfaceProvider.SurfaceCount > 0)
                        {
                            AirlockDisplays.Add(SurfaceProvider.GetSurface(0));
                        }
                    }
                }

            }//Ends AdditionalHardwareCheck


            public void WriteAirlockDisplays()
            {
                    foreach (IMyTextSurface AirlockDisplay in AirlockDisplays)
                    {
                        DrawDisplayUI(AirlockDisplay);
                    }
            }//Ends WriteAirlockDisplays

            public void DrawDisplayUI(IMyTextSurface DisplayScreen)
            {
                string AirlockTitle = $"{HardwareIdentifier} Airlock";

                //Oxygen tank data
                string OxygenTankTitle = "Oxygen Tank";
                Color OxygenTankColor = (OxygenTankAttached) ? Color.White : CustomGrey;
                Color OxygenTankFillBoxColor = Color.Red;
                int NumberOfFillBoxes = 0;
                if (OxygenTankAttached)
                {
                    if (OxygenTankFillPercentage >= 75)
                    {
                        NumberOfFillBoxes = 4;
                        OxygenTankFillBoxColor = Color.Blue;
                    }
                    else if (OxygenTankFillPercentage >= 50)
                    {
                        NumberOfFillBoxes = 3;
                        OxygenTankFillBoxColor = Color.Green;
                    }
                    else if (OxygenTankFillPercentage >= 25)
                    {
                        NumberOfFillBoxes = 2;
                        OxygenTankFillBoxColor = Color.Yellow;
                    }
                    else if (OxygenTankFillPercentage > 0)
                    {
                        NumberOfFillBoxes = 1;
                        OxygenTankFillBoxColor = Color.Red;
                    }
                    else
                    {
                        NumberOfFillBoxes = 0;

                    }
                }

                //Initial Setup of display
                var Surface = DisplayScreen;
                Surface.ScriptBackgroundColor = Color.Black;
                Surface.ContentType = ContentType.SCRIPT;
                Surface.Script = "";

                //Display Variables
                float FontScale = 0.7f;
                Vector2 TextureSize = Surface.TextureSize;
                Vector2 CanvasSize = Surface.SurfaceSize;
                Vector2 ViewPortOffset = (TextureSize - CanvasSize) / 2;
                Vector2 UsableSurfaceArea = new Vector2(CanvasSize.X - (Padding * 2f), CanvasSize.Y - (Padding * 2f));

                //Get height of text as a float
                Vector2 TextSize = Surface.MeasureStringInPixels(
                new StringBuilder("TextHeightScaleText"),
                "White",   // Font name
                FontScale   // Font scale
                );

                TextHeight = TextSize.Y;

                //Includes Padding and Viewportoffset
                Vector2 TextStart = new Vector2((ViewPortOffset.X) + Padding, (ViewPortOffset.Y + Padding));
                Vector2 AirlockTitleSize = GetTextSizeInformation(DisplayScreen, AirlockTitle, FontScale);
                Vector2 AirlockTitlePosition = new Vector2(Padding + ViewPortOffset.X + (AirlockTitleSize.X / 2f), TextStart.Y);

                //Status Box and Text Data
                Vector2 StatusTextSize = GetTextSizeInformation(DisplayScreen, AirlockStatus, FontScale);
                Vector2 StatusTextPosition = new Vector2(TextStart.X + (StatusTextSize.X / 2f), TextStart.Y + TextHeight + Padding);
                Vector2 StatusBoxPosition = new Vector2(ViewPortOffset.X + CanvasSize.X - Padding - StatusTextSize.Y, StatusTextPosition.Y + (StatusTextSize.Y / 2f));
                Vector2 StatusBoxSize = new Vector2(StatusTextSize.Y, StatusTextSize.Y);

                //Oxygen Sprite Data
                Vector2 TitleBoxSize = new Vector2(ViewPortOffset.X + (Padding / 2f) + UsableSurfaceArea.X, TextHeight + Padding);
                Vector2 TitleBoxPosition = new Vector2(ViewPortOffset.X + (Padding / 2f), ViewPortOffset.Y + Padding + (TextHeight / 2f));

                //Oxygen Sprite Data
                Vector2 OxygenTankTitleSize = GetTextSizeInformation(DisplayScreen, OxygenTankTitle, FontScale);
                Vector2 OxygenTankTitlePosition = new Vector2(TextStart.X + (OxygenTankTitleSize.X / 2f), ViewPortOffset.Y + CanvasSize.Y - Padding - TextHeight);
                Vector2 OxygenTankBoxPosition = new Vector2(TextStart.X + Padding + OxygenTankTitleSize.X, ViewPortOffset.Y + CanvasSize.Y - Padding - (TextHeight / 2f));
                float OxygenTankBoxWith = UsableSurfaceArea.X - (OxygenTankTitleSize.X + Padding);
                Vector2 OxygenTankBoxSize = new Vector2(OxygenTankBoxWith, TextHeight);

                //Oxygen tank fill boxes data
                float OxygenTankFillBoxWidth = (OxygenTankBoxWith - (3f * 5f)) / 4f;
                Vector2 OxygenTankFillBoxSize = new Vector2(OxygenTankFillBoxWidth, OxygenTankBoxSize.Y);
                Vector2 OxygenTankFillBoxPosition = new Vector2(OxygenTankBoxPosition.X, OxygenTankBoxPosition.Y);

                using (var Frame = Surface.DrawFrame())
                {

                    Frame.Add(new MySprite() //Title Box
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = TitleBoxPosition,
                        Size = TitleBoxSize,
                        Color = Color.Blue
                    });

                    Frame.Add(new MySprite() // Airlock Title
                    {
                        Type = SpriteType.TEXT,
                        Data = AirlockTitle,
                        Position = AirlockTitlePosition,
                        RotationOrScale = FontScale,
                        Alignment = TextAlignment.CENTER,
                        Color = Color.White,
                        FontId = "White"
                    });

                    Frame.Add(new MySprite() //Airlock Status Light Box
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = StatusBoxPosition,
                        Size = StatusBoxSize,
                        Color = CurrentLightColor
                    });

                    Frame.Add(new MySprite() //Airlock Status Text
                    {
                        Type = SpriteType.TEXT,
                        Data = AirlockStatus,
                        Position = StatusTextPosition,
                        RotationOrScale = FontScale,
                        Color = Color.White,
                        Alignment = TextAlignment.CENTER,
                        FontId = "White"
                    });

                    if (!OxygenTankAttached)
                    {
                        Frame.Add(new MySprite() //Oxygen Tank Box
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = OxygenTankBoxPosition,
                            Size = OxygenTankBoxSize,
                            Color = CustomGrey
                        });
                    }

                    Frame.Add(new MySprite() //Oxygen Tank Text
                    {
                        Type = SpriteType.TEXT,
                        Data = OxygenTankTitle,
                        Position = OxygenTankTitlePosition,
                        RotationOrScale = FontScale,
                        Color = OxygenTankColor,
                        Alignment = TextAlignment.CENTER,
                        FontId = "White"
                    });

                    for (int i = 0; i < NumberOfFillBoxes; i++)
                    {
                        Frame.Add(new MySprite() //Oxygen Tank Box
                        {
                            Type = SpriteType.TEXTURE,
                            Data = "SquareSimple",
                            Position = new Vector2(OxygenTankFillBoxPosition.X + (i * (5f + OxygenTankFillBoxWidth)), OxygenTankFillBoxPosition.Y),
                            Size = OxygenTankFillBoxSize,
                            Color = OxygenTankFillBoxColor
                        });
                    }
                }
            }//Ends DrawDisplayUI

            public Vector2 GetTextSizeInformation(IMyTextSurface Surface, string Text, float FontScale)
            {
                Vector2 TextSize = Surface.MeasureStringInPixels(
                new StringBuilder(Text),
                "White",   // Font name
                FontScale   // Font scale
                );

                return TextSize;
            }//Ends GetTextSizeInformation

            public void CheckForAtmosphere()
            {
                if (ExternalAirVent == null)
                {
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

            public void InitialHardwareSetup()
            {
                int InitializedBlockCount = 0;

                string AirlockVentIdentifier = HardwareIdentifier + " Air Vent";
                string ExteriorDoorIdentifier = HardwareIdentifier + " Exterior";
                string InteriorDoorIdentifier = HardwareIdentifier + " Interior";

                List<IMyDoor> AllDoors = new List<IMyDoor>();
                Program.GridTerminalSystem.GetBlocksOfType(AllDoors, Door => Door.CubeGrid == Program.Me.CubeGrid);

                //Check for exterior doors, and add door to list if name contains identifier
                if (ExteriorDoorGroup.Count == 0)
                {
                    Program.Echo($"Provide {HardwareIdentifier} Exterior Door(s)");
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
                else
                {
                    InitializedBlockCount++;
                }
                if (InteriorDoorGroup.Count == 0)
                {
                    Program.Echo($"Provide {HardwareIdentifier} Interior Door(s)");
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
                    Program.Echo($"Missing {HardwareIdentifier} Airlock Air Vent");
                    List<IMyAirVent> AllVents = new List<IMyAirVent>();
                    Program.GridTerminalSystem.GetBlocksOfType(AllVents, Vent => Vent.CubeGrid == Program.Me.CubeGrid);

                    foreach (IMyAirVent Vent in AllVents)
                    {
                        string VentName = Vent.CustomName.ToLower();
                        if (VentName.Contains(AirlockVentIdentifier.ToLower()))
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
                InitialSetupComplete = InitializationSuccess;
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

            }//Ends InitialBlockSetup

            public string GetHardwareIdentifier()
            {
                return HardwareIdentifier;
            } //Ends GetHardwareIdentifier

        }// Ends Airlock Class


        // Airlocks object list
        List<Airlock> Airlocks = new List<Airlock>();

        bool HardwareIdentifierProvided = false;
        bool AirlocksConstructed = false;
        List<string> HardwareIDs = new List<string>();
        IMyProgrammableBlock ProgrammableBlock;
        IMyCubeGrid CurrentGrid;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            ProgrammableBlock = Me;
            CurrentGrid = Me.CubeGrid;
        }//Ends Program

        public void Save()
        {

        } //Ends Save

        public void Main(string argument, UpdateType UpdateSource)
        {
            //Get ID tags
            if (!HardwareIdentifierProvided)
            {
                HardwareIdentifierProvided = CheckForHardwareIdentifier();
                return;
            }

            //Create airlocks once tags are provided
            if (!AirlocksConstructed)
            {
                BuildAirlocks();
                AirlocksConstructed = true;
            }

            foreach (Airlock Airlock in Airlocks)
            {
                //If the airlock isn't setup up, try to
                if (!Airlock.GetSetupCompletionStatus())
                {
                    Airlock.InitialHardwareSetup();
                }

                Airlock.UpdateAirlockInformation();
                Airlock.AirlockCycling();
                Airlock.AirlockLightManager();
                Airlock.CheckForAtmosphere();
                Airlock.WriteAirlockDisplays();
            }

            DistributeArguments(argument);


        }//Ends Main

        public void DistributeArguments(string Argument)
        {
            if (!(string.IsNullOrEmpty(Argument)))
            {
                Argument = Argument.ToLower();
                //Format for argument should be: ID:Command
                //ArgumentParts[0] = ID, ArgumentParts[1] = Command
                string[] ArgumentParts = Argument.Split(':');

                foreach (Airlock Airlock in Airlocks)
                {
                    string AirlockID = Airlock.GetHardwareIdentifier().ToLower();
                    if (ArgumentParts[0] == AirlockID)
                    {
                        if (ArgumentParts.Length > 1)
                        {
                            string Command = ArgumentParts[1];
                            Airlock.ProcessArguments(Command);
                        }
                    }
                }
            }
        }//Ends DistributeArgument

        public bool CheckForHardwareIdentifier()
        {
            string PBCustomData = ProgrammableBlock.CustomData.Trim();
            if (string.IsNullOrWhiteSpace(PBCustomData))
            {
                Echo("Provide Identifying Tag(s)");
                return false;
            }

            //Get ID tags into a list
            HardwareIDs = PBCustomData.Split(',').ToList();

            for (int i = 0; i < HardwareIDs.Count; i++)
            {
                //Remove extra spaces from the ID tags
                HardwareIDs[i] = HardwareIDs[i].Trim();
            }

            return true;
        }//Ends CheckForHardwareIdentifier

        public void BuildAirlocks()
        {
            //Clear the airlocks then build new ones
            Airlocks.Clear();
            foreach (string ID in HardwareIDs)
            {
                if (!string.IsNullOrWhiteSpace(ID))
                {
                    Airlock NewAirlock = new Airlock(this, ID);
                    Airlocks.Add(NewAirlock);
                }
            }

            //Start hardware setup for each airlock
            foreach (Airlock Airlock in Airlocks)
            {
                Airlock.InitialHardwareSetup();
            }

        }//Ends BuildAirlocks
    }//Ends Class
}//Ends NameSpace