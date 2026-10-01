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
using System.Diagnostics;
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

            //Animation Variables
            bool AnimationComplete = false;
            int AnimationTickCounter = 0;
            const int TotalAnimationTicks = 900; //15 seconds at 60 FPS

            int CurrentWaitingTicks = 0;
            const int TotalWaitingTicks = 1800;

            //Non-changing variables
            float Padding = 5f;
            float TextHeight;

            string HardwareIdentifier;
            double OxygenTankFillPercentage = 0.0;

            //Setup booleans
            bool InitialSetupComplete = false;
            bool HangarSetupComplete = false;
            bool AirlockPressurized = false;
            bool HangarPressurized = false;

            bool AirlockSealVerified = false;
            bool AirlockCycling = false;
            bool AirlockCycleRequested = false;
            bool HangarCycleRequested = false;

            bool AirlockInteriorDoorsClosed = false;
            bool AirlockExteriorDoorsClosed = false;
            bool HangarDoorsClosed = false;
            bool AACF = true; //Airlock Atmosphere Control Functionality
            bool ACCF = true; //Airlock Cycling Control Functionality
            bool HACF = true; //Hangar Atmosphere Control Functionality
            
            bool DisplaysProvided = false;
            bool AtmosphereCheck = false;
            bool OxygenTankProvided = false;
            bool OxygenTankFull = false;

            string [] AirlockModeNames = {"DEFAULT", "HANGAR", "MAINTENANCE"};
            string [] AtmosphereStatusNames = {"PRESSURIZING", "PRESSURIZED", "DEPRESSURIZING", "DEPRESSURIZED", "WORKING"};
            string [] CyclingStatusNames = {"INTERIOR", "--", "EXTERIOR"};
            string [] ACFNames = {"ENABLED", "DISABLED"};
            string [] CCFNames = {"ENABLED", "DISABLED"};

            string AirlockModeName = "";
            string AirlockAtmosphereStatusName = "";
            string AirlockCyclingStatusName = "";
            string AirlockTargetCyclingStatusName = "";
            string AACFName = "";
            string ACCFName = "";
            string HACFName = "";

            string HangarAtmosphereStatusName = "";
            string HangarCyclingStatusName = "";
            string HangarTargetCyclingStatusName = "";

            string AirlockCyclingAvailabilityText = "";

            //Airlock blocks
            IMyAirVent AirlockAirVent;

            //Airlock block lists
            List<IMyDoor> ExteriorAirlockDoors = new List<IMyDoor>();
            List<IMyDoor> InteriorAirlockDoors = new List<IMyDoor>();
            List<IMyDoor> AllAirlockDoors = new List<IMyDoor>();

            //Additional hardware block lists
            List<IMyInteriorLight> AirlockStatusLightGroup = new List<IMyInteriorLight>();
            List<IMyTextSurface> AirlockDisplays = new List<IMyTextSurface>();

            //Hangar blocks
            IMyAirVent HangarAirVent;

            //Hangar block lists
            List<IMyInteriorLight> HangarStatusLightGroup = new List<IMyInteriorLight>();
            List<IMyDoor> HangarDoors = new List<IMyDoor>();

            
            //Status Variables: 0 = Pressurizing, 1 = Pressurized, 2 = Depressurizing, 3 = Depressurized, 4 = Working, 5 = ACF Disabled
            int AirlockMode = 0; //0 = Default, 1 = Hangar Mode, 2 = Maintenance Mode

            int AirlockLightStatusNumber = 4;
            int AirlockAtmosphereStatusNumber = 4;
            int AirlockCyclingStatusNumber = 0;
            int AirlockTargetCyclingStatusNumber = 0;

            int HangarLightStatusNumber = 4;
            int HangarAtmosphereStatusNumber = 4;
            int HangarCyclingStatusNumber = 0;
            int HangarTargetCyclingStatusNumber = 0;

            //Light Data
            static readonly Color LogoColor = new Color(255, 20, 20);
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
            Color CurrentAirlockAvailabilityStatusColor;

            //Constructor
            public Airlock(Program program, string HardwareTag)
            {
                Program = program;
                HardwareIdentifier = HardwareTag;
            }

            //Hardware Methods
            public void InitialHardwareSetup()
            {
                int InitializedBlockCount = 0;

                string AirlockVentIdentifier = $"{HardwareIdentifier} Air Vent";
                string ExteriorDoorIdentifier = $"{HardwareIdentifier} Exterior";
                string InteriorDoorIdentifier = $"{HardwareIdentifier} Interior";

                //Check for exterior door(s)
                ExteriorAirlockDoors = Program.FindBlocks<IMyDoor>(ExteriorDoorIdentifier);
                InteriorAirlockDoors = Program.FindBlocks<IMyDoor>(InteriorDoorIdentifier);
                AirlockAirVent = Program.FindBlock<IMyAirVent>(AirlockVentIdentifier);

                if (ExteriorAirlockDoors.Count == 0)
                {
                    Program.Echo($"Provide {HardwareIdentifier} Exterior Door(s)");
                }
                else
                {
                    InitializedBlockCount++;
                }

                if (InteriorAirlockDoors.Count == 0)
                {
                    Program.Echo($"Provide {HardwareIdentifier} Interior Door(s)");
                }
                else
                {
                    InitializedBlockCount++;
                }

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

                    //Set status to Interior to begin
                    AirlockCyclingStatusNumber = 0;
                }

            }//Ends InitialBlockSetup

            //Getter Methods

            public void SetExternalAtmosphereStatus(bool value)
            {
                AtmosphereCheck = value;
            }//Ends SetExternalAtmosphereStatus

            public void SetOxygenTankStatistics(bool Provided, bool Value, double FillPercentage)
            {
                OxygenTankProvided = Provided;
                OxygenTankFull = Value;
                OxygenTankFillPercentage = FillPercentage;
            }//Ends SetOxygenTankStatistics

            public string GetHardwareIdentifier()
            {
                return HardwareIdentifier;
            } //Ends GetHardwareIdentifier

            public bool GetSetupCompletionStatus()
            {
                return InitialSetupComplete;
            }//Ends GetCompletionStatus

            public Vector2 GetTextSizeInformation(IMyTextSurface Surface, string Text, float FontScale)
            {
                Vector2 TextSize = Surface.MeasureStringInPixels(
                new StringBuilder(Text),
                "White",   // Font name
                FontScale   // Font scale
                );

                return TextSize;
            }//Ends GetTextSizeInformation

            //Step One
            public void UpdateAirlockInformation()
            {
                //Update Atmosphere check and set AACF and HACF states.
                ACFManager();
                //Update Cycle and Atmosphere Status field names
                UpdateVariableLabelNames();
                DisplaysProvided = (AirlockDisplays.Count > 0) ? true : false;

            }//Ends UpdateAirlockInformation

            public void UpdateVariableLabelNames()
            {
                AirlockCyclingStatusName = CyclingStatusNames[AirlockCyclingStatusNumber];
                AirlockTargetCyclingStatusName = CyclingStatusNames[AirlockTargetCyclingStatusNumber];
                AirlockModeName = AirlockModeNames[AirlockMode];

                HangarCyclingStatusName = CyclingStatusNames[HangarCyclingStatusNumber];
                HangarTargetCyclingStatusName = CyclingStatusNames[HangarTargetCyclingStatusNumber];

                AirlockCyclingAvailabilityText = (AirlockCycling) ? "CYCLING" : "READY\nTO\nCYCLE";

                AACFName = (AACF) ? "ENABLED" : "DISABLED";
                ACCFName = (ACCF) ? "ENABLED" : "DISABLED";
                HACFName = (HACF) ? "ENABLED" : "DISABLED";

            }//Update Variable Names

            public void ACFManager()
            {
                //If airlock is in default mode, then if there is external atmosphere, disable AACF
                //AtmosphereCheck will remain false even when there is no external airvent
                AACF = (AtmosphereCheck) ? false : true;
                HACF = (AtmosphereCheck) ? false : true;

                if (AirlockCycling)
                {
                    return; //Do not update status if currently pressurizing or depressurizing
                }

                if (AirlockAirVent.GetOxygenLevel() >= 0.75)
                {
                    AirlockAtmosphereStatusNumber = 1; //Pressurized
                    AirlockLightStatusNumber = 1; //Pressurized
                    AirlockPressurized = true;
                }

                if (AirlockAirVent.GetOxygenLevel() <= 0.1)
                {
                    AirlockAtmosphereStatusNumber = 3; //Depressurized
                    AirlockLightStatusNumber = 3; //Depressurized
                    AirlockPressurized = false;
                }

                AirlockAtmosphereStatusName = (AACF) ? AtmosphereStatusNames[AirlockAtmosphereStatusNumber] : $"AACF {AACFName}";

            }//Emds ACFManager

            //Step Two
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
                    HangarCycleRequested = true;
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
                if (AirlockMode == 0 || AirlockMode == 1)
                {
                    ACCF = true;
                }
                else if (AirlockMode == 2)
                {
                    ACCF = false;
                }

                AirlockModeName = AirlockModeNames[AirlockMode];
            }//Ends AirlockModeManager

            public void AdditionalHardwareCheck()
            {
                AirlockDisplays.Clear();
                DisplaysProvided = false;
                AnimationComplete = false;
                AnimationTickCounter = 0;

                string AirlockLightIdentifier = HardwareIdentifier + " Status";
                AirlockStatusLightGroup = Program.FindBlocks<IMyInteriorLight>(AirlockLightIdentifier);

                //check if buttons have the right name and if they have a screen
                foreach (IMyButtonPanel ButtonPanel in Program.FindBlocks<IMyButtonPanel>(HardwareIdentifier))
                {
                    if (!ButtonPanel.CustomName.ToLower().Contains("button panel"))
                    {
                        continue;
                    }

                    IMyTextSurfaceProvider SurfaceProvider = ButtonPanel as IMyTextSurfaceProvider;
                    if (SurfaceProvider != null && SurfaceProvider.SurfaceCount > 0)
                    {
                        AirlockDisplays.Add(SurfaceProvider.GetSurface(0));
                    }
                }

            }//Ends AdditionalHardwareCheck

            //Step Three
            public void ProcessCycling()
            {
                //If Cycling control disabled, do not continue, reset cycle and idle
                if (!ACCF)
                {
                    AirlockCycleRequested = false;
                    IdleAirlock();
                }

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
                        AirlockCycling = true;
                        AirlockSealVerified = false;
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

                if (AirlockMode == 2)
                {
                    if (HangarCycleRequested)
                    {
                        //Before cycling, Target cycle should already match current cycle
                        if (HangarTargetCyclingStatusNumber == HangarCyclingStatusNumber)
                        {
                            if (HangarCyclingStatusNumber == 0) //Cycle to exterior
                            {
                                HangarTargetCyclingStatusNumber = 2;
                            }
                            else if (HangarCyclingStatusNumber == 2) //Cycle to interior
                            {
                                HangarTargetCyclingStatusNumber = 0;
                            }

                            HangarCyclingStatusNumber = 1; //Set current status to cycling
                        }
                    }
                    else
                    {

                        if (AirlockTargetCyclingStatusNumber == 0)
                        {
                            CycleHangarInterior();
                        }
                        else if (AirlockTargetCyclingStatusNumber == 2)
                        {
                            CycleHangarExterior();
                        }
                    }
                }

            }//Ends ProcessCycling

            //Step Four
            public void UpdateLights()
            {
                Color[] LightColors = {Yellow, Green, Red, Red, Orange, Orange };
                CurrentAirlockLightColor = LightColors[AirlockLightStatusNumber];
                CurrentHangarLightColor = LightColors[HangarLightStatusNumber];
                CurrentAirlockAvailabilityStatusColor = (AirlockCycling) ? LightColors[0] : LightColors[1];

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

            public void VerifyAirlockSeal()
            {
                AirlockSealVerified = false;
                bool AllDoorsClosed = true;
                bool AirlockAirtight = false;

                foreach (IMyDoor Door in AllAirlockDoors)
                {
                    if (Door.Status != DoorStatus.Closed)
                    {
                        AllDoorsClosed = false;
                        break;
                    }
                }

                if (AirlockAirVent.CanPressurize)
                {
                    AirlockAirtight = true;
                }

                if (AllDoorsClosed && AirlockAirtight)
                {
                    AirlockSealVerified = true;
                }

            }//Ends VerifyAirlockSeal

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

            //Step Five
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

                string AirlockTitle = $"{HardwareIdentifier} AIRLOCK CONTROL TERMINAL";

                //Initial Setup of display
                var Surface = DisplayScreen;
                Surface.ScriptBackgroundColor = Color.Black;
                Surface.ContentType = ContentType.SCRIPT;
                Surface.Script = "";

                //Display Variables
                float FontScale = 0.5f;
                Vector2 TextureSize = Surface.TextureSize;
                Vector2 CanvasSize = Surface.SurfaceSize;
                Vector2 ViewPortOffset = (TextureSize - CanvasSize) / 2;

                //Get height of text as a float
                Vector2 TextSize = Surface.MeasureStringInPixels(
                new StringBuilder("TextHeightScaleText"),
                "White",   // Font name
                FontScale   // Font scale
                );

                TextHeight = TextSize.Y;
                //Title Sprite Data
                Vector2 TitleBoxSize = new Vector2((ViewPortOffset.X + CanvasSize.X) - (Padding * 2f), TextHeight + (Padding * 2f));
                Vector2 TitleBoxPosition = new Vector2(ViewPortOffset.X + Padding, ViewPortOffset.Y + (Padding * 2f) + (TextHeight / 2f));
                Vector2 AirlockTitlePosition = new Vector2((Padding * 2f) + ViewPortOffset.X, ViewPortOffset.Y + (Padding * 2f));

                //Cycling Availability Position and Size
                Vector2 CABoxSize = new Vector2(CanvasSize.X * 0.25f, (CanvasSize.Y - (TitleBoxSize.Y + Padding) - (Padding * 3f)) * 0.5f);
                Vector2 CABoxPosition = new Vector2(TitleBoxPosition.X, ViewPortOffset.Y + TitleBoxSize.Y + (Padding * 2f) + (CABoxSize.Y / 2f));

                float CATextHeight = (AirlockCycling) ? TextHeight : (TextHeight * 3f);
                Vector2 CATextPosition = new Vector2(CABoxPosition.X + Padding, CABoxPosition.Y - (CABoxSize.Y * 0.5f) + ((CABoxSize.Y - CATextHeight) * 0.5f));

                Vector2 AtmosphereStatusTextPosition = new Vector2(CABoxPosition.X + CABoxSize.X + Padding, CABoxPosition.Y - (CABoxSize.Y / 2f) + Padding);
                Vector2 CyclingStatusTextPosition = new Vector2(AtmosphereStatusTextPosition.X, CABoxPosition.Y - (CABoxSize.Y / 2f) + (CABoxSize.Y - (Padding * 4f) - (TextHeight * 3f)) + Padding + TextHeight);
                Vector2 ModeStatusTextPosition = new Vector2(AtmosphereStatusTextPosition.X, CABoxPosition.Y - (CABoxSize.Y / 2f) + ((CABoxSize.Y - (Padding * 4f) - (TextHeight * 3f)) * 2f) + Padding + (TextHeight * 2f));

                using (var Frame = Surface.DrawFrame())
                {
                    Frame.Add(new MySprite() //Title Box
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = TitleBoxPosition,
                        Size = TitleBoxSize,
                        Color = LogoColor
                    });

                    Frame.Add(new MySprite() // Airlock Title
                    {
                        Type = SpriteType.TEXT,
                        Data = AirlockTitle,
                        Position = AirlockTitlePosition,
                        RotationOrScale = FontScale,
                        Alignment = TextAlignment.LEFT,
                        Color = Color.White,
                        FontId = "White"
                    });

                    Frame.Add(new MySprite() // Cycling Availability Box
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Size = CABoxSize,
                        Position = CABoxPosition,
                        Color = CurrentAirlockAvailabilityStatusColor
                    });

                    Frame.Add(new MySprite() // Cycling Availability Text
                    {
                        Type = SpriteType.TEXT,
                        Data = AirlockCyclingAvailabilityText,
                        Position = CATextPosition,
                        RotationOrScale = FontScale,
                        Alignment = TextAlignment.LEFT,
                        Color = Color.Black,
                        FontId = "Debug"
                    });

                    Frame.Add(new MySprite() // Atmosphere Status Text Box
                    {
                        Type = SpriteType.TEXT,
                        Data = AirlockAtmosphereStatusName,
                        Position = AtmosphereStatusTextPosition,
                        RotationOrScale = FontScale,
                        Alignment = TextAlignment.LEFT,
                        Color = Color.White,
                        FontId = "White"
                    });

                    Frame.Add(new MySprite() // Cycling Status Text Box
                    {
                        Type = SpriteType.TEXT,
                        Data = AirlockCyclingStatusName,
                        Position = CyclingStatusTextPosition,
                        RotationOrScale = FontScale,
                        Alignment = TextAlignment.LEFT,
                        Color = Color.White,
                        FontId = "White"
                    });

                    Frame.Add(new MySprite() // Mode Status Text Box
                    {
                        Type = SpriteType.TEXT,
                        Data = $"MODE: {AirlockModeName}",
                        Position = ModeStatusTextPosition,
                        RotationOrScale = FontScale,
                        Alignment = TextAlignment.LEFT,
                        Color = Color.White,
                        FontId = "White"
                    });
                }
            }//Ends DrawDisplayUI

            public bool LoadRedFoxAnimation(IMyTextSurface DisplayScreen)
            {
                //Start at Black, fade in
                float LogoBrightness = (AnimationTickCounter <= 300) ? (AnimationTickCounter / 300f) : 1;
                Color CurrentLogoColor = Color.Multiply(LogoColor, LogoBrightness);


                var Surface = DisplayScreen;
                Surface.ScriptBackgroundColor = Color.Black;
                Surface.ContentType = ContentType.SCRIPT;
                Surface.Script = "";

                Vector2 TextureSize = Surface.TextureSize;
                Vector2 CanvasSize = Surface.SurfaceSize;
                Vector2 ViewPortOffset = (TextureSize - CanvasSize) / 2f;

                //In case the surface is not square
                float SmallerDimension = Math.Min(TextureSize.X, TextureSize.Y);

                Vector2 TriangleSize = new Vector2(SmallerDimension * 0.5f, SmallerDimension * 0.5f);
                Vector2 BoxSize = new Vector2(CanvasSize.X, TriangleSize.Y * 0.25f);

                Vector2 TrianglePosition = new Vector2(TextureSize.X / 2f, (TextureSize.Y / 2f) + (BoxSize.Y / 2f));
                Vector2 BoxPosition = new Vector2(TrianglePosition.X, TrianglePosition.Y + (TriangleSize.Y / 2f) - (BoxSize.Y / 2f) - 15f);

                //Size is half the triangle width, and height is half since its half a circle
                Vector2 SemiCircleSize = new Vector2(TriangleSize.X * 0.25f, TriangleSize.Y * 0.25f);
                Vector2 SemiCirclePosition = new Vector2(TrianglePosition.X, TrianglePosition.Y - (SemiCircleSize.Y / 2f));

                using (var frame = Surface.DrawFrame())
                {
                    frame.Add(new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "Triangle",
                        Position = TrianglePosition,
                        Size = TriangleSize,
                        Color = CurrentLogoColor,
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
                AnimationTickCounter += 10;
                if (AnimationTickCounter >= TotalAnimationTicks)
                {
                    return true; //Animation Complete
                }

                return false;
            }//Ends LoadRedFoxAnimation

            //Atmosphere Methods
            public void PressurizeAirlock()
            {
                //Pressurizing
                AirlockAtmosphereStatusNumber = 0;
                AirlockLightStatusNumber = 0;
                AirlockAtmosphereStatusName = AtmosphereStatusNames[AirlockAtmosphereStatusNumber];
                AirlockAirVent.Depressurize = false;
            }//Ends PressurizeAirlock

            public void DepressurizeAirlock()
            {
                //Depressurizing
                AirlockAtmosphereStatusNumber = 2;
                AirlockLightStatusNumber = 2;
                AirlockAtmosphereStatusName = AtmosphereStatusNames[AirlockAtmosphereStatusNumber];
                AirlockAirVent.Depressurize = true;
            }//Ends DepressurizeAirlock

            public void PressurizeHangar()
            {
                //Pressurizing
                HangarAtmosphereStatusNumber = 0;
                HangarLightStatusNumber = 0;
                HangarAtmosphereStatusName = AtmosphereStatusNames[HangarAtmosphereStatusNumber];
                HangarAirVent.Depressurize = false;

                if (HangarAirVent.GetOxygenLevel() >= 0.98)
                {
                    HangarAtmosphereStatusNumber = 1; //Pressurized
                    HangarLightStatusNumber = 1; //Pressurized
                    HangarAtmosphereStatusName = AtmosphereStatusNames[HangarAtmosphereStatusNumber];
                    HangarPressurized = true;
                }
            }//Ends PressurizeHangar

            public void DepressurizeHangar()
            {
                //Depressurizing
                HangarAtmosphereStatusNumber = 2;
                HangarLightStatusNumber = 2;
                HangarAtmosphereStatusName = AtmosphereStatusNames[HangarAtmosphereStatusNumber];
                HangarAirVent.Depressurize = true;


                if (HangarAirVent.GetOxygenLevel() <= 0.1 || OxygenTankFull)
                {
                    HangarAtmosphereStatusNumber = 3; //Depressurized
                    HangarLightStatusNumber = 3; //Depressurized
                    HangarAtmosphereStatusName = AtmosphereStatusNames[HangarAtmosphereStatusNumber];
                    HangarPressurized = false;
                }
            }//Ends DepressurizeHangar

            //Cycle Methods
            public void CycleHangarInterior()
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
                    //If no external atmosphere, pressurize hangar
                    if (HACF)
                    {
                        PressurizeHangar();

                        if (HangarPressurized)
                        {
                            //Once Hangar is Pressurized, disable cycling
                            AACF = false;
                        }
                    }

                    HangarCycleRequested = false;
                    HangarCyclingStatusNumber = 0;
                    HangarCyclingStatusName = CyclingStatusNames[HangarCyclingStatusNumber];
                }
            }//Ends CycleHangarInterior

            public void CycleHangarExterior()
            {
                if (HangarDoorsClosed)
                {
                    if (HACF)
                    {
                        if (HangarPressurized)
                        {
                            DepressurizeHangar();
                        }
                        else
                        {
                            HangarDoorsClosed = OpenDoors(HangarDoors);
                        }
                    }
                    else
                    {
                        HangarDoorsClosed = OpenDoors(HangarDoors);
                    }

                    //Enable AACF Regardless of atmosphere
                    ACCF = true;
                }
                else
                {
                    HangarCycleRequested = false;
                    HangarCyclingStatusNumber = 2;
                    HangarCyclingStatusName = CyclingStatusNames[HangarCyclingStatusNumber];
                }
            }//Ends CycleExterior

            public void CycleAirlockInterior()
            {
                if (!AirlockExteriorDoorsClosed)
                {
                    //Close doors first, then check if they are closed
                    //Use door #1 for comparison, as it must exist after initialization
                    //Check exterior doors as the interior doors will be closed already
                    if (AirlockSealVerified)
                    {
                        foreach (IMyDoor Door in AllAirlockDoors)
                        {
                            Door.Enabled = false;
                        }

                        AirlockExteriorDoorsClosed = CheckDoorStatus(ExteriorAirlockDoors);
                        AirlockInteriorDoorsClosed = CheckDoorStatus(InteriorAirlockDoors);
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

                        VerifyAirlockSeal();
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

                        //Check Doors regardless of AACF
                        AirlockExteriorDoorsClosed = CheckDoorStatus(ExteriorAirlockDoors);
                    }
                    else
                    {
                        AirlockCycleRequested = false;
                        AirlockCycling = false;
                        AirlockCyclingStatusNumber = 0;
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
                    if (AirlockSealVerified)
                    {
                        foreach (IMyDoor Door in AllAirlockDoors)
                        {
                            Door.Enabled = false;
                        }

                        AirlockExteriorDoorsClosed = CheckDoorStatus(ExteriorAirlockDoors);
                        AirlockInteriorDoorsClosed = CheckDoorStatus(InteriorAirlockDoors);
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

                        VerifyAirlockSeal();
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

                            if (!OxygenTankProvided)
                            {
                                // If no oxygen tank is assigned to check, if the tank is full, then the depressurization will be indefinite
                                AirlockPressurized = !(CountWaitingTime());
                            }

                            if (!AirlockPressurized || OxygenTankFull)
                            {
                                AirlockExteriorDoorsClosed = OpenDoors(ExteriorAirlockDoors);
                            }
                        }
                        else
                        {
                            AirlockExteriorDoorsClosed = OpenDoors(ExteriorAirlockDoors);
                        }

                        //Check Doors regardless of AACF
                        AirlockExteriorDoorsClosed = CheckDoorStatus(ExteriorAirlockDoors);
                    }
                    else
                    {
                        //Once doors are closed and airlock is depressurized, complete cycle.
                        AirlockCycleRequested = false;
                        AirlockCycling = false;
                        AirlockCyclingStatusNumber = 2;;
                    }
                }
            }//Ends CycleExterior

            public bool CountWaitingTime()
            {
                bool TimeExceeded = false;

                if (CurrentWaitingTicks >= TotalWaitingTicks)
                {
                    TimeExceeded = true;
                    CurrentWaitingTicks = 0;
                }
                else
                {
                    CurrentWaitingTicks += 10;
                }

                return TimeExceeded;
            }

            public bool CheckDoorStatus(List <IMyDoor> DoorList)
            {
                bool DoorsClosed = true;
                foreach(IMyDoor Door in DoorList)
                {
                    if (Door.Status != DoorStatus.Closed)
                    {
                        DoorsClosed = false;
                    }
                }

                return DoorsClosed;
            }//Ends CheckDoorStatus

            public bool OpenDoors(List<IMyDoor> DoorGroup)
            {
                foreach (IMyDoor Door in DoorGroup)
                {
                    Door.Enabled = true;
                    Door.ApplyAction("Open_On");
                }

                return false;
            }//Ends OpenDoors

            public void IdleAirlock()
            {
                if (AirlockExteriorDoorsClosed)
                {
                    AirlockExteriorDoorsClosed = OpenDoors(ExteriorAirlockDoors);
                }
                if (AirlockInteriorDoorsClosed)
                {
                    AirlockInteriorDoorsClosed = OpenDoors(InteriorAirlockDoors);
                }

            }//Ends IdleAirlock

            public void IdleHangar()
            {
                if (HangarDoorsClosed)
                {
                    HangarDoorsClosed = OpenDoors(HangarDoors);
                }

            }//Ends IdleHangar

        }// Ends Airlock Class

        // Airlocks object list
        List<Airlock> Airlocks = new List<Airlock>();

        bool HardwareIdentifierProvided = false;
        bool AirlocksConstructed = false;
        List<string> HardwareIDs = new List<string>();
        IMyProgrammableBlock ProgrammableBlock;
        IMyCubeGrid CurrentGrid;

        IMyGasTank PrimaryOxygenTank;
        IMyAirVent ExternalAirVent;
        bool ExternalAirVentProvided = false;
        bool PrimaryOxygenTankProvided = false;
        bool AtmosphereCheck = false;
        bool OxygenTankFull = false;
        double OxygenTankFillPercentage = 0;

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
                SharedHardwareCheck();
                BuildAirlocks();
                AirlocksConstructed = true;
            }

            if (PrimaryOxygenTankProvided)
            {
                CheckOxygenTankFillLevel();
            }
            
            if (ExternalAirVentProvided)
            {
                ExternalAtmosphereCheck();
            }

            foreach (Airlock Airlock in Airlocks)
            {
                //If the airlock isn't setup up, try to
                if (!Airlock.GetSetupCompletionStatus())
                {
                    Airlock.InitialHardwareSetup();
                    return;
                }

                Airlock.SetExternalAtmosphereStatus(AtmosphereCheck);
                Airlock.SetOxygenTankStatistics(PrimaryOxygenTankProvided, OxygenTankFull, OxygenTankFillPercentage);

                Airlock.UpdateAirlockInformation();
                PassArguments(Airlock, argument);
                Airlock.ProcessCycling();
                Airlock.UpdateLights();
                Airlock.WriteAirlockDisplays();
            }

        }//Ends Main

        public List<T> FindBlocks<T>(string Identifier) where T : class, IMyTerminalBlock
        {
            List<T> Found = new List<T>();
            string StandardizedIdentifier = Identifier.ToLower();
            GridTerminalSystem.GetBlocksOfType<T>(Found, Block => Block.CubeGrid == Me.CubeGrid && Block.CustomName.ToLower().Contains(StandardizedIdentifier));
            return Found;
        }//Ends FindBlocks

        public T FindBlock<T>(string Identifier) where T : class, IMyTerminalBlock
        {
            return FindBlocks<T>(Identifier).FirstOrDefault();
        }//Ends FindBlock

        public void SharedHardwareCheck()
        {
            ExternalAirVent = FindBlock<IMyAirVent>("External Air Vent");
            PrimaryOxygenTank = FindBlock<IMyGasTank>("Primary Oxygen Tank");

            if (ExternalAirVent != null)
            {
                ExternalAirVentProvided = true;
            }
            else
            {
                ExternalAirVentProvided = false;
                AtmosphereCheck = false;
            }

            if (PrimaryOxygenTank != null)
            {
                PrimaryOxygenTankProvided = true;
            }
            else
            {
                PrimaryOxygenTankProvided = false;
                OxygenTankFull = false;
                OxygenTankFillPercentage = 0;
            }
        }//Ends SharedHardwareCheck

        public void CheckOxygenTankFillLevel()
        {
            //Retrieve fill ration (0.0 to 1.0), then convert to percentage for comparison
            double OxygenTankFillRatio = PrimaryOxygenTank.FilledRatio;
            OxygenTankFillPercentage = OxygenTankFillRatio * 100;

            OxygenTankFull = (OxygenTankFillPercentage >= 98);
        }//Ends CheckOxygenTankLevel

        public void ExternalAtmosphereCheck()
        {
            //0.8f to reduce false positives
            float ExternalOxygenLevel = ExternalAirVent.GetOxygenLevel();
            AtmosphereCheck = (ExternalOxygenLevel >= 0.80f) ? true : false;

        }//Ends CheckForAtmosphere

        public void PassArguments(Airlock Airlock, string Argument)
        {
            if (!(string.IsNullOrEmpty(Argument)))
            {
                Argument = Argument.ToLower();
                //Format for argument should be: ID:Command
                //ArgumentParts[0] = ID, ArgumentParts[1] = Command
                string[] ArgumentParts = Argument.Split(':');

                string AirlockID = Airlock.GetHardwareIdentifier().ToLower();
                if (ArgumentParts[0] == AirlockID)
                {
                    if (ArgumentParts.Length > 1)
                    {
                        string Command = ArgumentParts[1];
                        Airlock.ProcessArguments(Command);

                        if (Command.Trim() == "update")
                        {
                            SharedHardwareCheck();
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