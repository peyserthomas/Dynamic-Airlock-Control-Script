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

            //Animation Variable
            bool AnimationComplete = false;
            int TickCounter = 0;
            const int TOTAL_TICKS = 300; //5 seconds at 60 FPS

            //Non-changing variables
            float Padding = 12f;
            float TextHeight;

            string HardwareIdentifier;
            double OxygenTankFillPercentage = 0.0;

            //Setup booleans
            bool InitialSetupComplete = false;
            bool HangarSetupComplete = false;
            bool AirlockPressurized = false;
            bool HangarPressurized = false;

            bool AirlockCycleRequested = false;
            bool HangarCycleRequested = false;
            bool AirlockInteriorDoorsClosed = false;
            bool AirlockExteriorDoorsClosed = false;
            bool HangarDoorsClosed = false;
            bool AACF = true; //Airlock Atmosphere Control Functionality
            bool HACF = true; //Hangar Atmosphere Control Functionality
            bool ACCF = true; //Airlock Cycling Control Functionality
            bool HCCF = true; //Hangar Cycling Control Functionality
            bool AtmosphereCheck = false;
            bool OxygenTankFull = false;
            bool DisplaysProvided = false;

            string [] AirlockModeNames = {"Default", "Hangar", "Maintenance"};
            string [] AtmosphereStatusNames = {"Pressurizing", "Pressurized", "Depressurizing", "Depressurized", "Working"};
            string [] CyclingStatusNames = {"Interior", "Cycling", "Exterior"};
            string [] ACFNames = {"Enabled", "Disabled"};
            string [] CCFNames = {"Enabled", "Disabled"};

            string AirlockModeName = "";
            string AirlockAtmosphereStatusName = "";
            string AirlockCyclingStatusName = "";
            string AirlockTargetCyclingStatusName = "";
            string HangarAtmosphereStatusName = "";
            string HangarCyclingStatusName = "";

            //Airlock blocks
            IMyAirVent AirlockAirVent;

            //Airlock block lists
            List<IMyDoor> ExteriorAirlockDoors = new List<IMyDoor>();
            List<IMyDoor> InteriorAirlockDoors = new List<IMyDoor>();
            List<IMyDoor> AllAirlockDoors = new List<IMyDoor>();

            //Additional hardware blocks
            IMyGasTank PrimaryOxygenTank;
            IMyAirVent ExternalAirVent;

            //Additional hardware block lists
            List<IMyInteriorLight> AirlockStatusLightGroup = new List<IMyInteriorLight>();
            List<IMyTextSurface> AirlockDisplays = new List<IMyTextSurface>();

            //Hangar blocks
            IMyAirVent HangarAirVent;

            //Hangar block lists
            List<IMyInteriorLight> HangarStatusLightGroup = new List<IMyInteriorLight>();
            List<IMyDoor> HangarDoors = new List<IMyDoor>();

            //Regular Airlock Cycles
            //Status Variables: 0 = Pressurizing, 1 = Pressurized, 2 = Depressurizing, 3 = Depressurized, 4 = Working, 5 = ACF Disabled
            int AirlockLightStatusNumber = 4;
            int HangarLightStatusNumber = 4;
            int AirlockAtmosphereStatusNumber = 4;
            int HangarAtmosphereStatusNumber = 4;
            int AirlockCyclingStatusNumber = 0;
            int AirlockTargetCyclingStatusNumber = 0;
            int HangarCyclingStatusNumber = 0;
            int AirlockMode = 0; //0 = Default, 1 = Hangar Mode, 2 = Maintenance Mode

            //Light Data
            static readonly Color Red = new Color(255, 0, 0); //Depressurizing
            static readonly Color Orange = new Color(255, 125, 0); //AFC Disabled
            static readonly Color Yellow = new Color(255, 220, 0); //Working
            static readonly Color Green = new Color(0, 255, 0); //Pressurizing
            static readonly Color CustomGrey = new Color(50, 50, 50); //Custom Grey
            
            static readonly float[] AirlockLightBlinkIntervals = {1f, 0f, 1f, 0f, 0f};
            static readonly float[] AirlockLightsBlinkLengths = {50f, 0f, 50f, 0f, 0f};
            static readonly float[] AirlockLightsBlinkOffsets = {0f, 0f, 0f, 0f, 0f};
            
            Color CurrentAirlockLightColor;
            Color CurrentHangarLightColor;

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

                //If ACF false, no need to continue
                if (argument == "cycle")
                {
                    AirlockCycleRequested = true;
                }

                if (argument == "cyclehangar")
                {
                    if (HangarCyclingStatusNumber == 0 || HangarCyclingStatusNumber == 2)
                    {
                        HangarCyclingStatusNumber = 1;
                        HangarCyclingStatusName = CyclingStatusNames[HangarCyclingStatusNumber];
                    }
                }

                if (argument == "update")
                {
                    AdditionalHardwareCheck();
                }

                if (argument == "toggle")
                {
                    UpdateAirlockMode();
                }
            }//Ends ProcessArguments
            public void ProcessCycling()
            {
                if (AirlockCycleRequested)
                {
                    //Before cycling, Target cycle should already match current cycle
                    if (AirlockTargetCyclingStatusNumber == AirlockCyclingStatusNumber)
                    {
                        if (AirlockCyclingStatusNumber == 0) //Cycle to exterior
                        {
                            AirlockTargetCyclingStatusNumber = 2;
                        }
                        else if (AirlockCyclingStatusNumber == 2) //Cycle to interior
                        {
                            AirlockTargetCyclingStatusNumber = 0;
                        }

                        AirlockCyclingStatusNumber = 1; //Set current status to cycling
                        AirlockCyclingStatusName = CyclingStatusNames[AirlockCyclingStatusNumber];
                    }
                    else
                    {
                        if (AirlockTargetCyclingStatusNumber == 0)
                        {
                            CycleAirlockInterior();
                        }
                        else if (AirlockTargetCyclingStatusNumber == 2)
                        {
                            CycleAirlockExterior();
                        }
                    }
                }

            }//Ends ProcessCycling

            /*public void PressurizeHangar()
            {
                if (!HangarDoorsLocked)
                {
                    if (HangarDoorGroup[0].Status == DoorStatus.Closed)
                    {
                        foreach (IMyDoor Door in HangarDoorGroup)
                        {
                            Door.Enabled = false;
                        }

                        //Lock hangar doors when closed and pressurize
                        HangarDoorsLocked = true;
                        HangarAirVent.Depressurize = false;
                    }
                    else
                    {
                        foreach (IMyDoor Door in HangarDoorGroup)
                        {
                            Door.ApplyAction("Open_Off");
                        }
                    }
                        
                }
                else
                {
                    if (HangarAirVent.GetOxygenLevel() >= 0.98)
                    {
                        foreach (IMyDoor Door in HangarDoorGroup)
                        {
                            Door.Enabled = true;
                            Door.ApplyAction("Open_On");
                        }

                        //Once the hangar is pressurized, turn off ACF and set status
                        AACF = false;
                        AirlockStatusNumber = 5;
                        AirlockStatus = StatusNames[AirlockStatusNumber];

                        HangarStatusNumber = 1; //Pressurized
                        HangarLightStatusNumber = 1; //Pressurized
                        HangarStatus = StatusNames[HangarStatusNumber];
                    }
                }
                
            }//Ends PressurizeHangar
            public void DepressurizeHangar()
            {
                if (HangarDoorsLocked)
                {
                    if (AirlockStatusNumber == 1)
                    {
                        HangarAirVent.Depressurize = true;
                    }
                    else
                    {
                        HangarDoorsLocked = false;
                    }

                    if (HangarAirVent.GetOxygenLevel() <= 0.1 || OxygenTankFull)
                    {

                    }
                    //Close doors first, then check if they are closed
                    if (HangarDoorGroup[0].Status == DoorStatus.Closed)
                    {
                        foreach (IMyDoor Door in HangarDoorGroup)
                        {
                            Door.Enabled = false;
                        }
                        //Start Depressurization
                        
                    }
                    else
                    {
                        //Close All Doors, check if they are closed, then lock.
                        foreach (IMyDoor Door in HangarDoorGroup)
                        {
                            Door.ApplyAction("Open_Off");
                        }
                    }
                }
                else
                {
                    if (HangarAirVent.GetOxygenLevel() <= 0.1 || OxygenTankFull)
                    {
                        foreach (IMyDoor Door in HangarDoorGroup)
                        {
                            Door.Enabled = true;
                            Door.ApplyAction("Open_On");
                        }

                        if (ExteriorAirlockDoors[0].Status == DoorStatus.Open)
                        {
                            AirlockExteriorDoorsClosed = false;
                            AirlockStatusNumber = 3; //Depressurized
                            AirlockLightStatusNumber = 3; //Depressurized
                            AirlockStatus = StatusNames[AirlockStatusNumber];
                        }
                    }
                }
                //stop here
                HangarLightStatusNumber = 0;

                if (HangarDoorsLocked)
                {
                    if (HangarDoorGroup[0].Status == DoorStatus.Closed)
                    {
                        foreach (IMyDoor Door in HangarDoorGroup)
                        {
                            Door.Enabled = false;
                        }

                        HangarDoorsLocked = true;
                        HangarAirVent.Depressurize = false;
                    }
                    else
                    {
                        foreach (IMyDoor Door in HangarDoorGroup)
                        {
                            Door.ApplyAction("Open_Off");
                        }
                    }

                }
                else
                {
                    if (HangarAirVent.GetOxygenLevel() >= 0.98)
                    {
                        foreach (IMyDoor Door in HangarDoorGroup)
                        {
                            Door.Enabled = true;
                            Door.ApplyAction("Open_On");
                        }

                        AACF = true;
                        HangarStatusNumber = 1; //Pressurized
                        HangarLightStatusNumber = 1; //Pressurized
                        HangarStatus = StatusNames[HangarStatusNumber];
                    }
                }
                HangarLightStatusNumber = 2;
            }//Ends PressurizeHangar*/

            public void VeryifyAirlockSeal()
            {

            }//Ends VerifyAirlockSeal

            public void UpdateAirlockMode()
            {
                //Increment mode, if greater than 2, reset to 0
                AirlockMode++;
                AirlockMode = (AirlockMode > 2) ? 0 : AirlockMode;
                AirlockModeName = AirlockModeNames[AirlockMode];

                AirlockModeManager();
            }//Ends UpdateAirlockMode

            public void AirlockModeManager()
            {
                if (AirlockMode == 2)
                {
                    
                }
            }//Ends AirlockModeManager

            public void CheckOxygenTankFillLevel()
            {
                if (PrimaryOxygenTank == null)
                {
                    return;
                }

                //Retrieve fill ration (0.0 to 1.0), then convert to percentage for comparison
                double OxygenTankFillRatio = PrimaryOxygenTank.FilledRatio;
                OxygenTankFillPercentage = OxygenTankFillRatio * 100;

                OxygenTankFull = (OxygenTankFillPercentage >= 98);
            }//Ends CheckOxygenTankLevel

            public void UpdateAirlockInformation()
            {
                //Check if Oxygen tank fill level
                CheckOxygenTankFillLevel();
                CheckForExternalAtmosphere();
                DisplaysProvided = (AirlockDisplays.Count > 0) ? true : false;

            }//Ends UpdateAirlockInformation

            public void UpdateLights()
            {
                Color[] LightColors = { Yellow, Green, Red, Red, Orange, Orange };
                CurrentAirlockLightColor = LightColors[AirlockLightStatusNumber];
                CurrentHangarLightColor = LightColors[HangarLightStatusNumber];
                AirlockLightManager(AirlockStatusLightGroup, AirlockLightStatusNumber, CurrentAirlockLightColor);

                if (AirlockMode == 1) 
                {
                    AirlockLightManager(HangarStatusLightGroup, HangarLightStatusNumber, CurrentHangarLightColor);
                }
            }//Ends UpdateLights

            public void AirlockLightManager(List<IMyInteriorLight> LightGroup, int LightStatusNumber, Color CurrentLightColor)
            {
                //Use status number to retrieve light data
                float CurrentBlinkTime = AirlockLightBlinkIntervals[LightStatusNumber];
                float CurrentBlinkLength = AirlockLightsBlinkLengths[LightStatusNumber];
                float CurrentBlinkOffset = AirlockLightsBlinkOffsets[LightStatusNumber];

                if (LightGroup.Count == 0)
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

            /*public void HangarInitialHardwareCheck()
            {
                int InitializedBlockCount = 0;
                HangarDoors.Clear();
                HangarAirVent = null;
                HangarSetupComplete = false;

                string HangarDoorIdentifier = HardwareIdentifier + " Hangar";
                string HangarAirVentIdentifier = HardwareIdentifier + "Hangar Air Vent";

                //Check for Hangar Doors
                GetDoors(HangarDoorIdentifier, HangarDoors);
                if (HangarDoors.Count == 0)
                {
                    Program.Echo($"Missing {HardwareIdentifier} Hangar Door(s)");
                }
                else
                {
                    InitializedBlockCount++;
                }

                //Check for Hangar vent
                HangarAirVent = GetVent(HangarAirVentIdentifier);
                if (HangarAirVent == null)
                {
                    Program.Echo($"Missing {HardwareIdentifier} Airlock Air Vent");
                }
                else
                {
                    InitializedBlockCount++;
                }

                HangarSetupComplete = (InitializedBlockCount == 2) ? true : false;
                if (HangarSetupComplete)
                {
                    if (HangarAirVent.GetOxygenLevel() >= 0.95)
                    {
                        HangarStatusNumber = 0; //Pressuring to begin
                        PressurizeHangar();
                    }
                    else
                    {
                        HangarStatusNumber = 2; //Depressurized to begin
                        DepressurizeHangar();
                    }

                    //Update Lights
                    AirlockLightManager(HangarStatusLightGroup, HangarLightStatusNumber, CurrentHangarLightColor);
                }

            }//Ends HangarHardwareCheck*/

            public IMyAirVent GetVent(string AirVentIdentifier)
            {
                IMyAirVent SearchedVent = null;

                List<IMyAirVent> AllVents = new List<IMyAirVent>();
                Program.GridTerminalSystem.GetBlocksOfType(AllVents, Vent => Vent.CubeGrid == Program.Me.CubeGrid);

                foreach (IMyAirVent Vent in AllVents)
                {
                    string VentName = Vent.CustomName.ToLower();
                    if (VentName.Contains(AirVentIdentifier.ToLower()))
                    {
                        SearchedVent = Vent;
                    }
                }

                return SearchedVent;
            }//Ends GetVent

            public void GetDoors(string DoorHardwareIdentifier, List<IMyDoor> DoorList)
            {
                List<IMyDoor> AllDoors = new List<IMyDoor>();
                Program.GridTerminalSystem.GetBlocksOfType(AllDoors, Door => Door.CubeGrid == Program.Me.CubeGrid);

                //Check for exterior doors, and add door to list if name contains identifier
                foreach (IMyDoor Door in AllDoors)
                {
                    //Ensure both the name and identifier are lowercase for comparison
                    string DoorName = Door.CustomName.ToLower();
                    if (DoorName.Contains(DoorHardwareIdentifier.ToLower()))
                    {
                        DoorList.Add(Door);
                    }
                }
            }//Ends GetDoor

            public void AdditionalHardwareCheck()
            {
                AirlockStatusLightGroup.Clear();
                AirlockDisplays.Clear();
                PrimaryOxygenTank = null;
                ExternalAirVent = null;
                DisplaysProvided = false;
                AnimationComplete = false;
                TickCounter = 0;

                ExternalAirVent = GetVent("External Air Vent");

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
                if (DisplaysProvided)
                {
                    foreach (IMyTextSurface AirlockDisplay in AirlockDisplays)
                    {
                        DrawDisplayUI(AirlockDisplay);
                    }
                }
            }//Ends WriteAirlockDisplays

            public void DrawDisplayUI(IMyTextSurface DisplayScreen)
            {
                //Keep playing animation until complete.
                if (!AnimationComplete && DisplaysProvided)
                {
                    AnimationComplete = LoadRedFoxAnimation(DisplayScreen);
                    return;
                }

                string AirlockTitle = $"{HardwareIdentifier} Airlock";

                //Oxygen tank data
                string OxygenTankTitle = "Oxygen Tank";
                Color OxygenTankColor = (PrimaryOxygenTank != null) ? Color.White : CustomGrey;
                Color OxygenTankFillBoxColor = Color.Red;
                int NumberOfFillBoxes = 0;
                if (PrimaryOxygenTank != null)
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
                    else if (OxygenTankFillPercentage > 1)
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
                Vector2 StatusTextSize = GetTextSizeInformation(DisplayScreen, AirlockAtmosphereStatusName, FontScale);
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

                    Frame.Add(new MySprite() //Airlock Atmosphere Status Light Box
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = StatusBoxPosition,
                        Size = StatusBoxSize,
                        Color = CurrentAirlockLightColor
                    });

                    Frame.Add(new MySprite() //Airlock Status Text
                    {
                        Type = SpriteType.TEXT,
                        Data = AirlockAtmosphereStatusName,
                        Position = StatusTextPosition,
                        RotationOrScale = FontScale,
                        Color = Color.White,
                        Alignment = TextAlignment.CENTER,
                        FontId = "White"
                    });

                    if (PrimaryOxygenTank == null)
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

            public void CheckForExternalAtmosphere()
            {
                if (ExternalAirVent == null)
                {
                    return;
                }

                //0.8f to reduce false positives
                float ExternalOxygenLevel = ExternalAirVent.GetOxygenLevel();
                AtmosphereCheck = (ExternalOxygenLevel >= 0.80f) ? true : false;
            }//Ends CheckForAtmosphere

            //ProcessCycling should only carry out pressurization/depressurization

            public void PressurizeAirlock()
            {
                //Pressurizing
                AirlockAtmosphereStatusNumber = 0;
                AirlockLightStatusNumber = 0;
                AirlockAtmosphereStatusName = AtmosphereStatusNames[AirlockAtmosphereStatusNumber];
                AirlockAirVent.Depressurize = false;

                if (AirlockAirVent.GetOxygenLevel() >= 0.98)
                {
                    AirlockAtmosphereStatusNumber = 1; //Pressurized
                    AirlockLightStatusNumber = 1; //Pressurized
                    AirlockAtmosphereStatusName = AtmosphereStatusNames[AirlockAtmosphereStatusNumber];
                    AirlockPressurized = true;
                }
            }//Ends PressurizeAirlock

            public void DepressurizeAirlock()
            {
                //Depressurizing
                AirlockAtmosphereStatusNumber = 2;
                AirlockLightStatusNumber = 2;
                AirlockAtmosphereStatusName = AtmosphereStatusNames[AirlockAtmosphereStatusNumber];
                AirlockAirVent.Depressurize = true;


                if (AirlockAirVent.GetOxygenLevel() <= 0.1 || OxygenTankFull)
                {
                    AirlockAtmosphereStatusNumber = 3; //Depressurized
                    AirlockLightStatusNumber = 3; //Depressurized
                    AirlockAtmosphereStatusName = AtmosphereStatusNames[AirlockAtmosphereStatusNumber];
                    AirlockPressurized = false;
                }
            }//Ends DepressurizeAirlock
            /*public void CycleHangarInterior()
            {
                if (!HangarDoorsClosed)
                {
                    //Close doors first, then check if they are closed
                    //Use door #1 for comparison, as it must exist after initialization
                    //Check exterior doors as the interior doors will be closed already
                    if (HangarDoors[0].Status == DoorStatus.Closed)
                    {
                        foreach (IMyDoor Door in HangarDoors)
                        {
                            Door.Enabled = false;
                        }

                        HangarDoorsClosed = true;
                    }
                    else
                    {
                        //Ensure doors are on to close them.
                        foreach (IMyDoor Door in HangarDoors)
                        {
                            Door.Enabled = true;
                        }

                        //Close All Doors, check if they are closed, then lock.
                        foreach (IMyDoor Door in HangarDoors)
                        {
                            Door.ApplyAction("Open_Off");
                        }
                    }
                }
                else
                {
                    if (AirlockInteriorDoorsClosed)
                    {
                        //If no external atmosphere, pressurize airlock
                        if (AACF)
                        {
                            PressurizeAirlock();

                            if (AirlockPressurized)
                            {
                                AirlockInteriorDoorsClosed = OpenDoors(InteriorAirlockDoors);
                            }
                        }
                        else
                        {
                            AirlockInteriorDoorsClosed = OpenDoors(InteriorAirlockDoors);
                        }
                    }
                    else
                    {
                        AirlockCycleRequested = false;
                        AirlockCyclingStatusNumber = 0;
                        AirlockCyclingStatusName = CyclingStatusNames[AirlockCyclingStatusNumber];
                    }
                }
            }//Ends CycleAirlockInterior*/

            public void CycleAirlockInterior()
            {
                if (!AirlockExteriorDoorsClosed)
                {
                    //Close doors first, then check if they are closed
                    //Use door #1 for comparison, as it must exist after initialization
                    //Check exterior doors as the interior doors will be closed already
                    if (ExteriorAirlockDoors[0].Status == DoorStatus.Closed)
                    {
                        foreach (IMyDoor Door in AllAirlockDoors)
                        {
                            Door.Enabled = false;
                        }

                        AirlockExteriorDoorsClosed = true;
                        AirlockInteriorDoorsClosed = true;
                    }
                    else
                    {
                        //Ensure doors are on to close them.
                        foreach (IMyDoor Door in ExteriorAirlockDoors)
                        {
                            Door.Enabled = true;
                        }

                        //Close All Doors, check if they are closed, then lock.
                        foreach (IMyDoor Door in AllAirlockDoors)
                        {
                            Door.ApplyAction("Open_Off");
                        }                        
                    }
                }
                else
                {
                    if (AirlockInteriorDoorsClosed)
                    {
                        //If no external atmosphere, pressurize airlock
                        if (AACF)
                        {
                            PressurizeAirlock();

                            if (AirlockPressurized)
                            {
                                AirlockInteriorDoorsClosed = OpenDoors(InteriorAirlockDoors);
                            }
                        }
                        else
                        {
                            AirlockInteriorDoorsClosed = OpenDoors(InteriorAirlockDoors);
                        }
                    }
                    else
                    {
                        AirlockCycleRequested = false;
                        AirlockCyclingStatusNumber = 0;
                        AirlockCyclingStatusName = CyclingStatusNames[AirlockCyclingStatusNumber];
                    }
                }
            }//Ends CycleAirlockInterior

            public void CycleAirlockExterior()
            {
                if (!AirlockInteriorDoorsClosed)
                {
                    //Close doors first, then check if they are closed
                    //Use door #1 for comparison, as it must exist after initialization
                    //Check exterior doors as the interior doors will be closed already
                    if (InteriorAirlockDoors[0].Status == DoorStatus.Closed)
                    {
                        foreach (IMyDoor Door in AllAirlockDoors)
                        {
                            Door.Enabled = false;
                        }

                        AirlockInteriorDoorsClosed = true;
                        AirlockExteriorDoorsClosed = true;
                    }
                    else
                    {
                        //Ensure doors are on to close them.
                        foreach (IMyDoor Door in InteriorAirlockDoors)
                        {
                            Door.Enabled = true;
                        }

                        //Close All Doors, check if they are closed, then lock.
                        foreach (IMyDoor Door in AllAirlockDoors)
                        {
                            Door.ApplyAction("Open_Off");
                        }
                    }
                }
                else
                {
                    if (AirlockExteriorDoorsClosed)
                    {
                        //If no external atmosphere, depressurize airlock
                        if (AACF)
                        {
                            DepressurizeAirlock();

                            if (!AirlockPressurized)
                            {
                                AirlockExteriorDoorsClosed = OpenDoors(ExteriorAirlockDoors);
                            }

                        }
                        else
                        {
                            AirlockExteriorDoorsClosed = OpenDoors(ExteriorAirlockDoors);
                        }
                    }
                    else
                    {
                        //Once doors are closed and airlock is depressurized, complete cycle.
                        AirlockCycleRequested = false;
                        AirlockCyclingStatusNumber = 2;
                        AirlockCyclingStatusName = CyclingStatusNames[AirlockCyclingStatusNumber];
                    }
                }
            }//Ends CycleExterior

            public bool OpenDoors(List<IMyDoor> DoorGroup)
            {
                foreach (IMyDoor Door in DoorGroup)
                {
                    Door.Enabled = true;
                    Door.ApplyAction("Open_On");
                }

                return false;
            }//Ends OpenDoors
            public bool LoadRedFoxAnimation(IMyTextSurface DisplayScreen)
            {
                var Surface = DisplayScreen;
                Surface.ScriptBackgroundColor = Color.Black;
                Surface.ContentType = ContentType.SCRIPT;
                Surface.Script = "";

                Vector2 TextureSize = Surface.TextureSize;
                Vector2 CanvasSize = Surface.SurfaceSize;
                Vector2 ViewPortOffset = (TextureSize - CanvasSize) / 2f;

                Vector2 TriangleSize = new Vector2(TextureSize.Y * 0.5f, TextureSize.Y * 0.5f);
                Vector2 BoxSize = new Vector2(CanvasSize.X, TriangleSize.Y * 0.25f);

                Vector2 TrianglePosition = new Vector2(TextureSize.X / 2f, (TextureSize.Y / 2f) + (BoxSize.Y / 2f));
                Vector2 BoxPosition = new Vector2(TrianglePosition.X, TrianglePosition.Y + (TriangleSize.Y / 2f) - (BoxSize.Y / 2f) - 15f);

                //Size is half the triangle width, and height is half since its half a circle
                Vector2 SemiCircleSize = new Vector2(TriangleSize.X * 0.25f, TriangleSize.Y * 0.25f);
                Vector2 SemiCirclePosition = new Vector2(TrianglePosition.X, TrianglePosition.Y);

                using (var frame = Surface.DrawFrame())
                {
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "Triangle",
                        Position = TrianglePosition,
                        Size = TriangleSize,
                        Color = Color.Red,
                        RotationOrScale = (float)Math.PI,
                        Alignment = TextAlignment.CENTER
                    });

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Size = BoxSize,
                        Position = BoxPosition,
                        Color = Color.Black,
                        Alignment = TextAlignment.CENTER
                    });

                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SemiCircle",
                        Size = SemiCircleSize,
                        Position = SemiCirclePosition,
                        Color = Color.Black,
                        Alignment = TextAlignment.CENTER,
                        RotationOrScale = (float)Math.PI
                    });
                }

                //Increment 10, as the script is Update10
                TickCounter += 10;
                if (TickCounter >= TOTAL_TICKS)
                {
                    return true; //Animation Complete
                }

                return false;
            }//Ends LoadAnimation

            public void InitialHardwareSetup()
            {
                int InitializedBlockCount = 0;

                string AirlockVentIdentifier = $"{HardwareIdentifier} Air Vent";
                string ExteriorDoorIdentifier = $"{ HardwareIdentifier} Exterior";
                string InteriorDoorIdentifier = $"{ HardwareIdentifier} Interior";

                ExteriorAirlockDoors.Clear();
                InteriorAirlockDoors.Clear();

                //Check for exterior door(s)
                GetDoors(ExteriorDoorIdentifier, ExteriorAirlockDoors);
                if (ExteriorAirlockDoors.Count == 0)
                {
                    Program.Echo($"Provide {HardwareIdentifier} Exterior Door(s)");
                }
                else
                {
                    InitializedBlockCount++;
                }

                //Check for interior door(s)
                GetDoors(InteriorDoorIdentifier, InteriorAirlockDoors);
                if (InteriorAirlockDoors.Count == 0)
                {
                    Program.Echo($"Provide {HardwareIdentifier} Interior Door(s)");
                }
                else
                {
                    InitializedBlockCount++;
                }

                //Check for necessary air vent
                AirlockAirVent = GetVent(AirlockVentIdentifier);
                if (AirlockAirVent == null)
                {
                    Program.Echo($"Missing {HardwareIdentifier} Airlock Air Vent");
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
                    AllAirlockDoors.AddRange(ExteriorAirlockDoors);
                    AllAirlockDoors.AddRange(InteriorAirlockDoors);
                    //Check for additional hardware one time after initialization
                    AdditionalHardwareCheck();
                    UpdateAirlockInformation();

                    //Cycle Initially.
                    AirlockCycleRequested = true;

                    if (AirlockAirVent.GetOxygenLevel() >= 0.95)
                    {
                        AirlockTargetCyclingStatusNumber = 0; //Interior to begin
                        CycleAirlockInterior();
                    }
                    else
                    {
                        AirlockTargetCyclingStatusNumber = 2; //Exterior to begin
                        CycleAirlockExterior();
                    }

                    //Update Lights
                    AirlockLightManager(AirlockStatusLightGroup, AirlockLightStatusNumber, CurrentAirlockLightColor);
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
                Airlock.ProcessCycling();
                Airlock.UpdateLights();
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
                Echo("Provide Hardware Identifier(s)");
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