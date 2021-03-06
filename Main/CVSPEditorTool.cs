﻿using UnityEngine;
using System.IO;
using System;
using CarnationVariableSectionPart.UI;
using System.Collections;
using KSP.Localization;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine.UIElements;
using System.Text;

namespace CarnationVariableSectionPart
{

    public class FPSDisplay : MonoBehaviour
    {
        float deltaTime = 0.0f;
        private GUIStyle style;
        private Rect rect;

        void Update()
        {
            deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        }
        private void Start()
        {
            int w = Screen.width, h = Screen.height;
            rect = new Rect(0, 0, w, h * 2 / 100);
            style = new GUIStyle
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = h * 5 / 100
            };
            //new Color (0.0f, 0.0f, 0.5f, 1.0f);
            style.normal.textColor = Color.red;
            style.fontStyle = FontStyle.Bold;
        }
        void OnGUI()
        {
            float msec = deltaTime * 1000.0f;
            float fps = 1.0f / deltaTime;
            float red = 1 - Mathf.Clamp01((fps - 20f) / 40f);
            style.normal.textColor = new Color(red, 1 - red, 0);
            string text = string.Format("{0:0.0} ms ({1:0.} fps)", msec, fps);
            GUI.Label(rect, text, style);
        }
    }
    public class CVSPEditorTool : MonoBehaviour
    {
#if UNITYEDITOR
        public static string AssemblyPath = @"E:\SteamLibrary\steamapps\common\Kerbal Space Program\GameData\CarnationVariableSectionPart\Plugins";

#else
        public static string AssemblyPath = typeof(CVSPEditorTool).Assembly.Location;
#endif
        public static KeyCode ToggleKey = KeyCode.F;
        public static string CreatorDefaultTankType;
        private static readonly HandleGizmo.ModifierType[] types = new HandleGizmo.ModifierType[] {
        HandleGizmo.ModifierType.XAndY,
        HandleGizmo.ModifierType.XAndY,
        HandleGizmo.ModifierType.XAndY,
        HandleGizmo.ModifierType.XAndY,
        HandleGizmo.ModifierType.Stretch,
        HandleGizmo.ModifierType.Rotation,
        HandleGizmo.ModifierType.XAndY,
        HandleGizmo.ModifierType.XAndY,
        HandleGizmo.ModifierType.XAndY,
        HandleGizmo.ModifierType.XAndY,
        HandleGizmo.ModifierType.Y,
        HandleGizmo.ModifierType.X,
        HandleGizmo.ModifierType.Y,
        HandleGizmo.ModifierType.X };

        private static Camera editorCamera;
        private static float GAME_HIGHLIGHTFACTOR;
        private static readonly float MinScale = 0.5f;
        private static readonly float MaxScale = 2f;
        private static readonly string FileName_Handles = "cvsp_x64.handles";
        private static readonly string FileName_GUI = "cvsp_x64.gui";
        private static GameObject prefab;
        private GameObject partSection0, partSection1;
        private GameObject section0, section1;
        public bool mouseDragging = false;
        private float lastTwist;
        private HandleGizmo currentHandle;
        internal ModuleCarnationVariablePart gizmoEditingPart;
        private bool? sec0FacingUser;
        private bool? sec1FacingUser;
        private static CVSPEditorTool _Instance;
        public static bool EditorToolActivated = false;
        private static GameObject _gameObject = null;
        private static Shader bumpedSpecShader;
        private static float Twist, Tilt0, Tilt1, Length, Run, Raise, Section0Width, Section0Height, Section1Width, Section1Height;
        private static float[] CornerRadius = new float[8];

        private static bool WaitingForPartAttach;
        private static Part CVSPPartSpawned;
        private static CVSPPartInfo CVSPInfoSpawned;


        public static CVSPEditorTool Instance
        {
            get
            {
                if (_gameObject == null)
                {
                    _gameObject = new GameObject("CVSPPersist");
                    _gameObject.SetActive(false);
                    _Instance = _gameObject.AddComponent<CVSPEditorTool>();
                    //  DontDestroyOnLoad(new GameObject().AddComponent<FPSDisplay>().gameObject);
                    DontDestroyOnLoad(_gameObject);
                    // Debug.Log("[CRFP] Editor Tool Loaded");
                }
                return _Instance;
            }
        }

        public static Camera EditorCamera
        {
            get
            {
#if UNITYEDITOR
                if (Camera.current == null)
                    editorCamera = Camera.main;
                else
                    editorCamera = Camera.current;
#else
                if (EditorLogic.fetch == null)
                    Debug.LogError("EditorLogic.fetch == null");
                else
                    editorCamera = EditorLogic.fetch.editorCamera;
#endif
                return editorCamera;
            }
        }

        public static Shader PartShader
        {
            get
            {
                if (bumpedSpecShader == null)
                {
                    var i = Instance;
                    i.gameObject.SetActive(true);
                    i.Load();
                }
                return bumpedSpecShader;
            }
        }

        public static bool CopyParameters { get; private set; }

        private void Start()
        {
            GameEvents.onPartAttach.Add(new EventData<GameEvents.HostTargetAction<Part, Part>>.OnEvent(ApplyTransformForCreatedCVSP));
            GameEvents.onVesselLoaded.Add((Vessel v) =>
            {
                if (HighLogic.LoadedSceneIsEditor && GameUILocked)
                {
                    EditorLogic.fetch.Unlock("CVSPEditorTool");
                    GameUILocked = false;
                }
            });
            GameEvents.OnVesselRollout.Add((ShipConstruct c) =>
            {
                CVSPUIManager.Instance.Close();
                CVSPUIManager.Instance.LockGameUI(false);
            });
        }

        //Local symetry string "Local"
        private static readonly string autoLOC_6001218 = Localizer.GetStringByTag("#autoLOC_6001218");
        /// <summary>
        /// 
        /// </summary>
        /// <param name="data">target:self, host:parent</param>
        private void ApplyTransformForCreatedCVSP(GameEvents.HostTargetAction<Part, Part> data)
        {
            if (WaitingForPartAttach)
            {
                if (CVSPPartSpawned)
                {
                    CVSPPartSpawned.transform.position = CVSPInfoSpawned.position;
                    CVSPPartSpawned.transform.rotation = CVSPInfoSpawned.orientation;
                    //CVSPPartSpawned.FindModuleImplementing<ModuleCarnationVariablePart>().tankType = CreatorDefaultTankType;

                    int symCount = CVSPPartSpawned.symmetryCounterparts.Count;
                    if (symCount > 0)
                    {
                        Vector3 symAixs;
                        Vector3 symRootPos;
                        Transform symRootTransform;
                        if (EditorLogic.fetch.coordSpaceText.text == autoLOC_6001218)
                        {
                            if (!CVSPPartSpawned.parent) return;
                            symRootTransform = CVSPPartSpawned.parent.partTransform;
                        }
                        else
                        {
                            symRootTransform = EditorLogic.fetch.ship.parts[0].localRoot.partTransform;
                        }
                        symAixs = symRootTransform.up;
                        symRootPos = symRootTransform.position;
                        if (CVSPPartSpawned.symMethod == SymmetryMethod.Radial)
                        {
                            var angleIncrement = 360f / (symCount + 1);
                            for (int i = 0; i < CVSPPartSpawned.symmetryCounterparts.Count;)
                            {
                                Part p = CVSPPartSpawned.symmetryCounterparts[i];
                                //p.FindModuleImplementing<ModuleCarnationVariablePart>().tankType = CreatorDefaultTankType;
                                p.partTransform.rotation = CVSPPartSpawned.partTransform.rotation;
                                p.partTransform.position = CVSPPartSpawned.partTransform.position;
                                p.partTransform.RotateAround(symRootPos, symAixs, angleIncrement * (++i));
                            }
                        }
                        else
                        {
                            Part symPart = CVSPPartSpawned.symmetryCounterparts[0];
                            if (!symPart) return;
                            var cvsp = symPart.FindModuleImplementing<ModuleCarnationVariablePart>();
                            if (cvsp)
                            {
                                // cvsp.tankType = CreatorDefaultTankType;
                                cvsp.scale = new Vector3(cvsp.scale.x, cvsp.scale.y, -cvsp.scale.z);

                                CVSPPartSpawned.partTransform.rotation.ToAngleAxis(out float angle, out Vector3 axis);
                                var localAxis = symRootTransform.InverseTransformVector(axis);
                                axis = symRootTransform.TransformVector(new Vector3(-localAxis.x, localAxis.y, localAxis.z));
                                symPart.partTransform.rotation = Quaternion.AngleAxis(-angle, axis);
                                var offset = CVSPPartSpawned.partTransform.position - symRootPos;
                                var localOffset = symRootTransform.InverseTransformVector(offset);
                                offset = symRootTransform.TransformVector(new Vector3(-localOffset.x, localOffset.y, localOffset.z));
                                symPart.partTransform.position = offset + symRootPos;
                            }
                        }
                    }

                    WaitingForPartAttach = false;
                }
            }
        }

        internal void FixZeroSizeBug()
        {
            if (CVSPUIManager.Instance)
            {
                CVSPUIManager.Instance.Radius0 = CVSPUIManager.Instance.Radius1 = 1f;
                CVSPUIManager.Instance.Radius2 = CVSPUIManager.Instance.Radius3 = 1f;
            }
        }

        private void Load()
        {
            if (prefab == null && !assetLoading)
            {
                assetLoading = true;
                // Debug.Log("[CRFP] Start loading assetbundles");
                LoadGizmoAssets();
                LoadGUIAssets();
                assetLoading = false;
                CreateGizmos();
                section0.SetActive(false);
                section1.SetActive(false);
            }
        }

        private void LoadGUIAssets()
        {
            string path = AssemblyPath;
            path = path.Remove(path.LastIndexOf("Plugins")) + @"AssetBundles" + Path.DirectorySeparatorChar + FileName_GUI;
            StartCoroutine(LoadGUICoroutine(path));
        }

        IEnumerator LoadGUICoroutine(string path)
        {
            AssetBundle b = AssetBundle.LoadFromFile(path);
            GameObject original = b.LoadAsset<GameObject>("Canvas");
            SectionPreviewRenderer.renderMat = Instantiate(b.LoadAsset<Material>("RenderShader"));
            EdgeVisualizer.mat =/* new Material*/(b.LoadAsset<Material>("lines"));
            CapsuleRay.getPartModel += (Collider col) =>
            {
                var part = col.GetComponentInParent<Part>();
                if (!part) return null;

                Transform model = part.partTransform.Find("model");
                MeshFilter[] mf = model.GetComponentsInChildren<MeshFilter>();
                return mf;
            };
            var canvas = Instantiate(original);
            b.Unload(false);

            CVSPUIManager.onValueChanged += UI_TextureSelectedOrParamModified;
            CVSPUIManager.getLocalizedString += (string tag) =>
            {
                if (Localizer.TryGetStringByTag(tag, out string s))
                    return s;
                else return tag;
            };
            CVSPUIManager.getSnapAndFineTuneState += (ref bool snap, ref bool finetune) =>
            {
                snap = GameSettings.VAB_USE_ANGLE_SNAP;
                finetune = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            };
            CVSPUIManager.getEditorCamera += () => EditorCamera;
            CVSPUIManager.createCVSP += SpawnCVSP;
            CVSPUIManager.postGameScreenMsg += (string s) => ScreenMessages.PostScreenMessage(s, 3f, ScreenMessageStyle.UPPER_RIGHT, Color.magenta);
            CVSPUIManager.lockGameUI += (bool loc) =>
            {
                if (loc)
                {
                    if (!GameUILocked)
                    {
                        //参考了B9PW里面的用法，起源ferram4
                        EditorLogic.fetch.Lock(false, false, false, "CVSPEditorTool");
                        GameUILocked = true;
                    }
                }
                else if (GameUILocked)
                {
                    EditorLogic.fetch.Unlock("CVSPEditorTool");
                    GameUILocked = false;
                }
            };
            CVSPUIManager.determineWhichToModify += DetermineWhichToModify;
            CVSPUIManager.getGameLanguage += () => GameSettings.LANGUAGE;

            if (!CVSPConfigs.RealFuel)
                CVSPUIManager.Instance.resources.Instance.OnItemSwitched += (string s) =>
            {
                if (!uiEditingPart) return;
                if (uiEditingPart)
                {
                    uiEditingPart.tankType = s;
                    uiEditingPart.uiEditing = true;
                    //uiEditingPart.UpdateCostWidget();
                    //foreach (var item in uiEditingPart.part.symmetryCounterparts)
                    //{
                    //    item.FindModuleImplementing<ModuleCarnationVariablePart>().UpdateCostWidget();
                    //}
                    // lastUIModifiedPart.UpdateFuelTank();
                }
            };

            var tankTypeAbbrNames = new string[CVSPConfigs.TankDefinitions.Count];
            for (int i = 0; i < CVSPConfigs.TankDefinitions.Count; i++)
                tankTypeAbbrNames[i] = CVSPConfigs.TankDefinitions[i].abbrName;
            if (!CVSPConfigs.RealFuel)
                CVSPUIManager.Instance.resources.Instance.RefreshItems(tankTypeAbbrNames);
            CVSPResourceSwitcher.OnGetRealFuelInstalled += () => CVSPConfigs.RealFuel;

            TextureDefinitionSwitcher.DefaultItem = CVSPConfigs.TextureSets.First();
            yield return new WaitUntil(() => CVSPUIManager.Instance && CVSPUIManager.Instance.endsTextures && CVSPUIManager.Instance.sideTextures);
            CVSPUIManager.Instance.endsTextures.RefreshItems(CVSPConfigs.TextureSets.ToArray());
            CVSPUIManager.Instance.sideTextures.RefreshItems(CVSPConfigs.TextureSets.ToArray());
            CVSPUIManager.Instance.endsTextures.OnItemSwitched += EndsTexturesSwitched;
            CVSPUIManager.Instance.sideTextures.OnItemSwitched += SideTexturesSwitched;

            CVSPUIManager.SectionCorners = CVSPConfigs.SectionCornerDefinitions.ToArray();
            CVSPUIManager.OnSectionCornerChanged += (int id, int target) =>
                {
                    if (uiEditingPart)
                    {
                        uiEditingPart.uiEditing = true;
                        uiEditingPart.SetCornerTypes(CVSPConfigs.SectionCornerDefinitions[id], target);
                    }
                };
            CVSPUIManager.GetMaxSize += (out float max, out float length) =>
              {
                  max = CVSPConfigs.MaxSize;
                  length = CVSPConfigs.MaxLength;
              };

            CVSPCreatePartPanel.onDeactivateGizmos += Deactivate;

            CVSPToggleLink.OnToggleLink += (int id, bool link) =>
              {
                  if (uiEditingPart)
                      if (id == 0)
                          uiEditingPart.LinkSection0 = link;
                      else
                          uiEditingPart.LinkSection1 = link;
              };

            yield return new WaitForSecondsRealtime(0.5f);
            // yield return new WaitUntil(() => CVSPPresetManager.Instance);
            CVSPPresetManager.RenderPreview += (SectionInfo info) => SectionPreviewRenderer.Instance.RenderSectionPreview(info, uiEditingPart);
            if (!EditorToolActivated || !HighLogic.LoadedSceneIsEditor)
                Instance.gameObject.SetActive(false);
            // Debug.Log("[CRFP] GUI loaded");
        }

        private void SideTexturesSwitched(TextureSetDefinition selected)
        {
            if (uiEditingPart)
            {
                //    if (!selected.diff) throw new Exception();
                string path;
                string dir = selected.directory.Replace('/', '\\');
                path = dir + '\\' + selected.diffuse;
                uiEditingPart.SetTexture(selected.diff, TextureTarget.SideDiff, path);
                path = dir + '\\' + selected.normals;
                uiEditingPart.SetTexture(selected.norm, TextureTarget.SideNorm, path);
                path = dir + '\\' + selected.specular;
                uiEditingPart.SetTexture(selected.spec, TextureTarget.SideSpec, path);
                uiEditingPart.UpdateMaterials();
                foreach (var p in uiEditingPart.part.symmetryCounterparts)
                {
                    var m = p.FindModuleImplementing<ModuleCarnationVariablePart>();
                    if (m)
                    {
                        m.CopyMaterialFrom(uiEditingPart);
                        m.UpdateMaterials();
                    }
                }
                CVSPUIManager.Instance.OnTextureDefinitionChanged(TextureTarget.SideDiff, selected);
                CVSPUIManager.Instance.OnTextureDefinitionChanged(TextureTarget.SideNorm, selected);
                CVSPUIManager.Instance.OnTextureDefinitionChanged(TextureTarget.SideSpec, selected);
            }
        }

        private void EndsTexturesSwitched(TextureSetDefinition selected)
        {
            if (uiEditingPart)
            {
                //  if (selected.name == null || selected.name == string.Empty) throw new Exception();
                //
                string path;
                string dir = selected.directory.Replace('/', '\\');
                dir = (dir.Length > 0 ? (dir + '\\') : "");
                path = dir + selected.diffuse;
                uiEditingPart.SetTexture(selected.diff, TextureTarget.EndsDiff, path);
                path = dir + selected.normals;
                uiEditingPart.SetTexture(selected.norm, TextureTarget.EndsNorm, path);
                path = dir + selected.specular;
                uiEditingPart.SetTexture(selected.spec, TextureTarget.EndsSpec, path);
                uiEditingPart.UpdateMaterials();
                foreach (var p in uiEditingPart.part.symmetryCounterparts)
                {
                    var m = p.FindModuleImplementing<ModuleCarnationVariablePart>();
                    if (m)
                    {
                        m.CopyMaterialFrom(uiEditingPart);
                        m.UpdateMaterials();
                    }
                }
                CVSPUIManager.Instance.OnTextureDefinitionChanged(TextureTarget.EndsDiff, selected);
                CVSPUIManager.Instance.OnTextureDefinitionChanged(TextureTarget.EndsNorm, selected);
                CVSPUIManager.Instance.OnTextureDefinitionChanged(TextureTarget.EndsSpec, selected);
            }
        }

        bool DetermineWhichToModify()
        {
            if (gizmoEditingPart)
            {
                uiEditingPart = gizmoEditingPart;
            }
            else
            {
                if (lastUIModifiedPart)
                    uiEditingPart = lastUIModifiedPart;
                else
                {
                    CVSPUIManager.Instance.Close();
                    CVSPUIManager.Instance.LockGameUI(false);
                    return false;
                }
            }
            return true;
        }
        private void UI_TextureSelectedOrParamModified(Texture2D t2d, TextureTarget target, string path)
        {
            //uiEditingPart is determined in CVSPUIManager's Update
            if (!uiEditingPart) return;
            //Debug.Log("Called time" + calledtame + " part y:" + uiEditingPart.part.transform.position.y); ;
            uiEditingPart.uiEditing = true;

            var oldEnds    /**/   = uiEditingPart.endTexNames;
            var oldSide    /**/   = uiEditingPart.sideTexNames;
            var useEnds    /**/   = uiEditingPart.UseEndsTexture;
            var useSide    /**/   = uiEditingPart.UseSideTexture;
            var oldShine   /**/   = uiEditingPart.Shininess;
            var oldTint    /**/   = uiEditingPart.colorTint;
            if (t2d)
            {//选择了贴图
                uiEditingPart.SetTexture(t2d, target, path);
                uiEditingPart.UpdateMaterials();
                foreach (var p in uiEditingPart.part.symmetryCounterparts)
                {
                    var m = p.FindModuleImplementing<ModuleCarnationVariablePart>();
                    if (m)
                    {
                        m.CopyMaterialFrom(uiEditingPart);
                        m.UpdateMaterials();
                    }
                }
            }
            else
            {//参数调整
                var ui = CVSPUIManager.Instance;
                uiEditingPart.Section0Width      /**/   = ui.Section0Width;
                uiEditingPart.Section0Height     /**/   = ui.Section0Height;
                uiEditingPart.Section1Width      /**/   = ui.Section1Width;
                uiEditingPart.Section1Height     /**/   = ui.Section1Height;
                uiEditingPart.Length             /**/   = ui.Length;
                uiEditingPart.Twist              /**/   = ui.Twist;
                uiEditingPart.Tilt0              /**/   = ui.Tilt0;
                uiEditingPart.Tilt1              /**/   = ui.Tilt1;
                uiEditingPart.Run                /**/   = ui.Run;
                uiEditingPart.Raise              /**/   = ui.Raise;
                uiEditingPart.SideOffsetU        /**/   = ui.SideOffsetU;
                uiEditingPart.SideOffsetV        /**/   = ui.SideOffsetV;
                uiEditingPart.EndOffsetU         /**/   = ui.EndOffsetU;
                uiEditingPart.EndOffsetV         /**/   = ui.EndOffsetV;
                uiEditingPart.SideScaleU         /**/   = ui.SideScaleU;
                uiEditingPart.SideScaleV         /**/   = ui.SideScaleV;
                uiEditingPart.EndScaleU          /**/   = ui.EndScaleU;
                uiEditingPart.EndScaleV          /**/   = ui.EndScaleV;
                uiEditingPart.TintR              /**/   = ui.TintR;
                uiEditingPart.TintG              /**/   = ui.TintG;
                uiEditingPart.TintB              /**/   = ui.TintB;
                uiEditingPart.Shininess          /**/   = ui.Shininess;
                uiEditingPart.UseSideTexture     /**/   = ui.UseSideTexture;
                uiEditingPart.UseEndsTexture     /**/   = ui.UseEndsTexture;
                uiEditingPart.CornerUVCorrection /**/   = ui.CornerUVCorrection;
                uiEditingPart.RealWorldMapping   /**/   = ui.RealWorldMapping;
                uiEditingPart.EndsTiledMapping   /**/   = ui.EndsTiledMapping;
                uiEditingPart.Physicless = ui.Physicless;
                uiEditingPart.OptimizeEnds = ui.OptimizeEnds;
                var r = ui.Radius;
                for (int i = 0; i < r.Length; i++)
                {
                    uiEditingPart.SetCornerRadius(i, r[i]);
                }
                if (gizmoEditingPart)
                    CopyParamsFormPart();
            }
            if (oldTint != uiEditingPart.colorTint || !oldEnds.Equals(uiEditingPart.endTexNames) || !oldSide.Equals(uiEditingPart.sideTexNames) ||
                useEnds != uiEditingPart.UseEndsTexture || useSide != uiEditingPart.UseSideTexture || oldShine != uiEditingPart.Shininess)
            {
                uiEditingPart.UpdateMaterials();
                foreach (var p in uiEditingPart.part.symmetryCounterparts)
                {
                    var m = p.FindModuleImplementing<ModuleCarnationVariablePart>();
                    if (m)
                    {
                        m.CopyMaterialFrom(uiEditingPart);
                        m.UpdateMaterials();
                    }
                }
            }
        }

        internal void CopyParamsToUI(ModuleCarnationVariablePart cvsp = null)
        {
            if (!cvsp)
                cvsp = gizmoEditingPart;
            if (!cvsp) return;
            var ui = CVSPUIManager.Instance;
            if (!ui) return;
            CVSPUIManager.paramTransferedTime = Time.unscaledTime;
            ui.Section0Width      /**/   = cvsp.Section0Width;
            ui.Section0Height     /**/   = cvsp.Section0Height;
            ui.Section1Width      /**/   = cvsp.Section1Width;
            ui.Section1Height     /**/   = cvsp.Section1Height;
            ui.Length             /**/   = cvsp.Length;
            ui.Twist              /**/   = cvsp.Twist;
            ui.Tilt0              /**/   = cvsp.Tilt0;
            ui.Tilt1              /**/   = cvsp.Tilt1;
            ui.Run                /**/   = cvsp.Run;
            ui.Raise              /**/   = cvsp.Raise;
            ui.SideOffsetU        /**/   = cvsp.SideOffsetU;
            ui.SideOffsetV        /**/   = cvsp.SideOffsetV;
            ui.EndOffsetU         /**/   = cvsp.EndOffsetU;
            ui.EndOffsetV         /**/   = cvsp.EndOffsetV;
            ui.SideScaleU         /**/   = cvsp.SideScaleU;
            ui.SideScaleV         /**/   = cvsp.SideScaleV;
            ui.EndScaleU          /**/   = cvsp.EndScaleU;
            ui.EndScaleV          /**/   = cvsp.EndScaleV;
            ui.TintR              /**/   = cvsp.TintR;
            ui.TintG              /**/   = cvsp.TintG;
            ui.TintB              /**/   = cvsp.TintB;
            ui.tintR.UpdateRGBBackgrrounds();
            ui.Shininess          /**/   = cvsp.Shininess;
            ui.UseSideTexture     /**/   = cvsp.UseSideTexture;
            ui.UseEndsTexture     /**/   = cvsp.UseEndsTexture;
            ui.CornerUVCorrection /**/   = cvsp.CornerUVCorrection;
            ui.RealWorldMapping   /**/   = cvsp.RealWorldMapping;
            ui.EndsTiledMapping   /**/   = cvsp.EndsTiledMapping;
            ui.Physicless = cvsp.Physicless;
            ui.OptimizeEnds = cvsp.OptimizeEnds;

            string str = cvsp.tankType;

            if (!CVSPConfigs.RealFuel)
                if (CVSPUIManager.Instance.resources.Instance) CVSPUIManager.Instance.resources.Instance.SwitchTo(str);
                else CVSPUIManager.Instance.resources.Instance.DefaultItem = str;
            if (CVSPUIManager.Instance.endsTextures) CVSPUIManager.Instance.endsTextures.SwitchTo(default);
            if (CVSPUIManager.Instance.sideTextures) CVSPUIManager.Instance.sideTextures.SwitchTo(default);
            var r = new float[8];
            for (int i = 0; i < 8; i++) r[i] = cvsp.GetCornerRadius(i);
            ui.Radius = r;

            for (int i1 = 0; i1 < cvsp.GetCornerTypes().Length; i1++)
                CVSPUIManager.Instance.cornerTypes[i1].text = cvsp.GetCornerTypes()[i1].name;

            CVSPToggleLink.Set(cvsp.LinkSection0, cvsp.LinkSection1);
        }


        static bool assetLoading = false;
        public static int GizmosLayer = 0b10;
        public void SetGizmosLayer(int l)
        {
            GizmosLayer = l;
            if (gizmoEditingPart != null)
            {
                var t = gizmoEditingPart.Model.transform;
                for (int i = 0; i < t.childCount; i++)
                {
                    var tt = t.GetChild(i);
                    for (int j = 0; j < tt.childCount; j++)
                    {
                        tt.GetChild(j).gameObject.layer = GizmosLayer;
                    }
                }
            }
        }

        void LoadGizmoAssets()
        {

            string path = @"file://" + AssemblyPath;
            path = path.Remove(path.LastIndexOf("Plugins")) + @"AssetBundles" + Path.DirectorySeparatorChar;
            WWW www = new WWW(path + FileName_Handles);

            if (www.error != null)
                Debug.LogError("[CRFP] Loading... " + www.error);
            if (bumpedSpecShader == null)
            {
                bumpedSpecShader = Shader.Find("KSP/Bumped Specular (Mapped)");
                if (bumpedSpecShader != null)
                    ;//    Debug.Log("[CRFP] Bumped Specular shader loaded from game memory");
                else
                    Debug.Log("[CRFP] Bumped Specular(Mapped) shader not found!!");
            }
            prefab = www.assetBundle.LoadAsset("handles") as GameObject;
            if (prefab != null)
            {
                var t = prefab.transform;
                Quaternion q = Quaternion.Euler(0f, 0f, 90f);
                for (int i = 0; i < t.childCount; i++)
                {
                    var g = t.GetChild(i).gameObject;
                    g.transform.localPosition = q * g.transform.localPosition;
                    g.transform.localRotation = q * g.transform.localRotation;
                    g.layer = GizmosLayer;
                    var c = g.AddComponent<HandleGizmo>();
                    //MakeMeshReadable(g);
                    c.type = types[i];
                    c.ID = i;
                }
            }
            www.assetBundle.Unload(false);
            www.Dispose();
        }
        private void Activate_(ModuleCarnationVariablePart module)
        {
            if (!EditorToolActivated)
            {
                /*foreach (var i in module.part.localRoot.ship.Parts)
                {
                    var model = i.partTransform.Find("model");
                    if (model)
                    {
                        var cols = model.GetComponentsInChildren<Collider>();
                        if (cols != null)
                            foreach (var col in cols)
                            {
                                Debug.Log($"part: {i.name}, col: {col.gameObject.name}, acti: {col.enabled} lyr: {col.gameObject.layer}");
                            }
                    }
                }*/

                if (lastUIModifiedPart)
                    lastUIModifiedPart.CleanGizmos();
                lastUIModifiedPart = gizmoEditingPart = module;
                gizmoEditingPart.OnStartGizmosEdit();
                if (CopyParameters) gizmoEditingPart.CorrectTwistAndTilts(oldTwist, oldTilts);
                SetupGizmos();
                CalcGizmosSize();

                _gameObject.SetActive(true);
                EditorToolActivated = true;
                GameUILocked = false;
                CopyParamsFormPart();
                CopyParameters = false;
                GAME_HIGHLIGHTFACTOR = GameSettings.PART_HIGHLIGHTER_BRIGHTNESSFACTOR;
                Highlighting.Highlighter.HighlighterLimit = 0.2f;
                StopAllCoroutines();
                StartCoroutine(GizmosToUICoroutine());

                CopyParamsToUI();
                CVSPUIManager.Instance.SetTexFileNames(ModuleCarnationVariablePart.SplitStringByComma(gizmoEditingPart.endTexNames, 3), ModuleCarnationVariablePart.SplitStringByComma(gizmoEditingPart.sideTexNames, 3));
            }
        }

        IEnumerator GizmosToUICoroutine()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(0.1f);
                yield return new WaitForEndOfFrame();
                if (requestUIUpdate)
                {
                    requestUIUpdate = false;
                    CopyParamsToUI();
                }
            }
        }

        internal void Deactivate()
        {
            if (EditorToolActivated)
            {
                Highlighting.Highlighter.HighlighterLimit = GAME_HIGHLIGHTFACTOR;
                _gameObject.SetActive(false);
                EditorToolActivated = false;
                mouseDragging = false;
                GameUILocked = false;
                RemoveEvents(partSection0);
                RemoveEvents(partSection1);
                partSection0 = null;
                partSection1 = null;
                if (currentHandle) currentHandle.OnRelease();
                for (int i = 0; i < gizmoEditingPart.Model.transform.childCount; i++)
                {
                    var t = gizmoEditingPart.Model.transform.GetChild(i);
                    if (t.name.StartsWith("section"))
                        if (t.name.EndsWith("0node"))
                            while (t.childCount > 0)
                                t.GetChild(0).SetParent(section0.transform, false);
                        else if (t.name.EndsWith("1node"))
                            while (t.childCount > 0)
                                t.GetChild(0).SetParent(section1.transform, false);
                    if (section0.transform.childCount == 14)
                        if (section1.transform.childCount == 14)
                            break;
                }
                if (gizmoEditingPart) gizmoEditingPart.OnEndGizmoEdit();
                gizmoEditingPart = null;
                StopAllCoroutines();
            }
        }
        internal static ModuleCarnationVariablePart RaycastCVSP()
        {
            if (Time.time - LastCastTime > Time.deltaTime)
            {
                //var list = new List<ModuleCarnationVariablePart>();
                //foreach (var item in c.part.ship.Parts)
                //    if (item.name.StartsWith("CarnationREDFlexible"))
                //    {
                //        var cvsp = item.FindModuleImplementing<ModuleCarnationVariablePart>();
                //        if (cvsp != null)
                //        {
                //            cvsp.gameObject.layer = ModuleCarnationVariablePart.CVSPEditorLayer;
                //            list.Add(cvsp);
                //        }
                //    }


                Ray r;
                r = EditorCamera.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(r, out RaycastHit hit, 100f, 1 << ModuleCarnationVariablePart.CVSPEditorLayer))
                {
                    //part是模型的父级(model)的父级
                    //不能用hit.transform，不准确
                    var go = hit.collider.transform.parent.parent;
                    if (!go) return null;
                    var cvsp = go.GetComponent<ModuleCarnationVariablePart>();
                    return cvsp;
                }
                LastCastTime = Time.time;

                //if (list != null)
                //    foreach (var i in list)
                //        if (i) i.gameObject.layer = default;
            }
            return null;
        }

        internal static void ActivateWithoutGizmos(ModuleCarnationVariablePart cvsp)
        {
            if (cvsp)
            {
                if (EditorToolActivated)
                    Activate(cvsp);
                else
                {
                    Instance.lastUIModifiedPart = Instance.uiEditingPart = cvsp;
                    cvsp.OnStartUIEdit();

                    cvsp.StartCoroutine(SetupUI(cvsp));
                }
            }
        }
        static IEnumerator SetupUI(ModuleCarnationVariablePart cvsp)
        {
            yield return new WaitUntil(() => CVSPUIManager.Initialized);
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            CVSPUIManager.Instance.Open();
            //CVSPUIManager.Instance.Expand();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            Instance.CopyParamsToUI(cvsp);
            CVSPUIManager.Instance.SetTexFileNames(ModuleCarnationVariablePart.SplitStringByComma(cvsp.endTexNames, 3), ModuleCarnationVariablePart.SplitStringByComma(cvsp.sideTexNames, 3));
        }


        internal static void Activate(ModuleCarnationVariablePart cvsp)
        {
            if (cvsp)
                if (EditorToolActivated)
                {
                    Instance.CopyParamsFormPart();
                    var old = Instance.gizmoEditingPart;
                    Instance.Deactivate();
                    if (cvsp != Instance.gizmoEditingPart)
                    {
                        CopyParameters = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                        //按下左Ctrl则复制参数
                        if (CopyParameters)
                        {
                            oldTwist = cvsp.Twist;
                            oldTilts = new Vector2(cvsp.Tilt0, cvsp.Tilt1);
                            CopyParamsTo(cvsp);
                            var linkedNode = ModuleCarnationVariablePart.LinkedSectionID(cvsp, old);
                            if (linkedNode >= 0) cvsp.LinkSections(linkedNode);
                            Instance.CopyParamsToUI(cvsp);
                        }
                        //参数复制后，再启动编辑
                        Instance.Activate_(cvsp);
                    }
                }
                else
                    Instance.Activate_(cvsp);
        }

        private void CopyParamsFormPart()
        {
            Twist = gizmoEditingPart.Twist;
            Tilt0 = gizmoEditingPart.Tilt0;
            Tilt1 = gizmoEditingPart.Tilt1;
            Raise = gizmoEditingPart.Raise;
            Run = gizmoEditingPart.Run;
            Length = gizmoEditingPart.Length;
            Section0Height = gizmoEditingPart.Section0Height;
            Section0Width = gizmoEditingPart.Section0Width;
            Section1Height = gizmoEditingPart.Section1Height;
            Section1Width = gizmoEditingPart.Section1Width;
            CornerRadius[0] = gizmoEditingPart.Section0Radius.x;
            CornerRadius[1] = gizmoEditingPart.Section0Radius.y;
            CornerRadius[2] = gizmoEditingPart.Section0Radius.z;
            CornerRadius[3] = gizmoEditingPart.Section0Radius.w;
            CornerRadius[4] = gizmoEditingPart.Section1Radius.x;
            CornerRadius[5] = gizmoEditingPart.Section1Radius.y;
            CornerRadius[6] = gizmoEditingPart.Section1Radius.z;
            CornerRadius[7] = gizmoEditingPart.Section1Radius.w;
        }

        private static void CopyParamsTo(ModuleCarnationVariablePart cvsp)
        {
            cvsp.Section0Height =   /**/ Section0Height;
            cvsp.Section0Width =    /**/ Section0Width;
            cvsp.Section1Height =   /**/ Section1Height;
            cvsp.Section1Width =    /**/ Section1Width;
            cvsp.Twist =            /**/ Twist;
            cvsp.Tilt0 =            /**/ Tilt0;
            cvsp.Tilt1 =            /**/ Tilt1;
            cvsp.Raise =            /**/ Raise;
            cvsp.Run =              /**/ Run;
            cvsp.Length =           /**/ Length;
            cvsp.Section0Radius.x = /**/ CornerRadius[0];
            cvsp.Section0Radius.y = /**/ CornerRadius[1];
            cvsp.Section0Radius.z = /**/ CornerRadius[2];
            cvsp.Section0Radius.w = /**/ CornerRadius[3];
            cvsp.Section1Radius.x = /**/ CornerRadius[4];
            cvsp.Section1Radius.y = /**/ CornerRadius[5];
            cvsp.Section1Radius.z = /**/ CornerRadius[6];
            cvsp.Section1Radius.w = /**/ CornerRadius[7];


            var old = Instance.lastUIModifiedPart;
            cvsp.colorTint          /**/ = (old.colorTint);
            cvsp.Shininess          /**/ = old.Shininess;
            cvsp.EndsDiffTexture    /**/ = old.EndsDiffTexture;
            cvsp.EndsNormTexture    /**/ = old.EndsNormTexture;
            cvsp.EndsSpecTexture    /**/ = old.EndsSpecTexture;
            cvsp.SideDiffTexture    /**/ = old.SideDiffTexture;
            cvsp.SideNormTexture    /**/ = old.SideNormTexture;
            cvsp.SideSpecTexture    /**/ = old.SideSpecTexture;
            cvsp.mappingOptions     /**/ = old.mappingOptions;
            cvsp.uvOffsets          /**/ = (old.uvOffsets);
            cvsp.uvScales           /**/ = (old.uvScales);
            cvsp.Physicless = old.Physicless;
            cvsp.OptimizeEnds = old.OptimizeEnds;

            cvsp.SectionCorners = old.SectionCorners;
        }

/*        internal static void OnPartDestroyed()
        {
         //if (_Instance != null)
         //    Instance.Deactivate();
        }*/
        private static float LastCastTime;
        private bool fineTune;
        private static float angleSnap;
        private static float offsetInterval;
        internal bool GameUILocked;
        private static float oldTwist;
        private static Vector2 oldTilts;
        private bool requestUIUpdate;
        internal ModuleCarnationVariablePart lastUIModifiedPart;
        internal ModuleCarnationVariablePart uiEditingPart;

        private void OnGizmoEdit(float value0, float value1, int id, int sectionID)
        {
            requestUIUpdate = true;
            Vector2 dir;
            int targetID;
            switch (id)
            {
                case 0:  //X or Y or XAndY
                case 1:  //X or Y or XAndY
                case 2:  //X or Y or XAndY
                case 3:  //X or Y or XAndY
                case 10: //X or Y or XAndY
                case 11: //X or Y or XAndY
                case 12: //X or Y or XAndY
                case 13: //X or Y or XAndY
                    if (id >= 10)
                        id -= 10;
                    if (id < 2)
                    {
                        value0 *= -1;
                        value1 *= id == 0 ? 1 : -1;
                    }
                    else
                        value1 *= id == 3 ? 1 : -1;
                    if (Input.GetKey(KeyCode.LeftControl))
                        value0 = value1 = Mathf.Sign(value1 + value0) * Mathf.Max(Mathf.Abs(value0), Mathf.Abs(value1));
                    if (sectionID == 0)
                    {
                        Section0Width += value0;
                        Section0Height += value1;
                    }
                    else
                    {
                        Section1Width += value0;
                        Section1Height += value1;
                    }
                    if (sectionID == 0)
                    {
                        Section0Width = Mathf.Clamp(Section0Width, 0, CVSPConfigs.MaxSize);
                        Section0Height = Mathf.Clamp(Section0Height, 0, CVSPConfigs.MaxSize);
                        gizmoEditingPart.Section0Width = OffsetSnap(Section0Width);
                        gizmoEditingPart.Section0Height = OffsetSnap(Section0Height);
                    }
                    else
                    {
                        Section1Width = Mathf.Clamp(Section1Width, 0, CVSPConfigs.MaxSize);
                        Section1Height = Mathf.Clamp(Section1Height, 0, CVSPConfigs.MaxSize);
                        gizmoEditingPart.Section1Width = OffsetSnap(Section1Width);
                        gizmoEditingPart.Section1Height = OffsetSnap(Section1Height);
                    }
                    break;
                case 4://Length Modifier
                    Length += sectionID > 0 ? value0 : value0;
                    Length = Mathf.Clamp(Length, 0, CVSPConfigs.MaxLength);
                    gizmoEditingPart.Length = OffsetSnap(Length);
                    break;
                case 5://Twist 
                    Twist = lastTwist + (sectionID > 0 ? value0 : value0);
                    Twist = Mathf.Clamp(Twist, -45, 45);
                    gizmoEditingPart.Twist = AngleSnap(Twist);
                    break;
                case 6://XAndY, Radius modifiers
                    dir = Quaternion.Euler(0, 0, gizmoEditingPart.scale.z > 0 ? 0 : 90) * new Vector2(-0.707106f, 0.707106f);
                    targetID = 0;
                    goto IL_3;
                case 7:
                    dir = Quaternion.Euler(0, 0, gizmoEditingPart.scale.z > 0 ? 0 : 90) * new Vector2(-0.707106f, -0.707106f);
                    targetID = 3;
                    goto IL_3;
                case 8:
                    dir = Quaternion.Euler(0, 0, gizmoEditingPart.scale.z > 0 ? 0 : 90) * new Vector2(0.707106f, -0.707106f);
                    targetID = 2;
                    goto IL_3;
                case 9:
                    dir = Quaternion.Euler(0, 0, gizmoEditingPart.scale.z > 0 ? 0 : 90) * new Vector2(0.707106f, 0.707106f);
                    targetID = 1;
                IL_3:
                    var modifier = .4f * Vector2.Dot(new Vector2(value0, value1), dir);

                    if (sectionID == 1)
                    {
                        if (id % 2 == 0)
                            targetID += 2;
                        targetID += 3;
                    }
                    if (gizmoEditingPart.scale.z < 0)
                        if ((targetID + sectionID) % 2 == 1)
                            modifier = -modifier;
                    float radius = Mathf.Clamp(CornerRadius[targetID] - modifier, -1, 1);
                    float snappedRadius = OffsetSnap(CornerRadius[targetID]);
                    bool applyAll = Input.GetKey(KeyCode.LeftShift);
                    bool applySection = Input.GetKey(KeyCode.LeftControl);
                    bool applyEdge = Input.GetKey(KeyCode.LeftAlt);
                    gizmoEditingPart.SetCornerRadius(targetID, snappedRadius);
                    CornerRadius[targetID] = radius;
                    int i = 0, l = 0;
                    if (applyAll)
                    {
                        i = 0;
                        l = 8;
                    }
                    else if (applySection)
                    {
                        i = targetID > 3 ? 4 : 0;
                        l = i + 4;
                    }
                    else if (applyEdge)
                    {
                        i = targetID - (targetID > 3 ? 4 : -4);
                        l = i + 1;
                    }
                    for (; i < l; i++)
                    {
                        CornerRadius[i] = radius;
                        gizmoEditingPart.SetCornerRadius(i, snappedRadius);
                    }
                    break;
            }
        }

        private static float OffsetSnap(float val)
        {
            if (GameSettings.VAB_USE_ANGLE_SNAP)
                return Mathf.Round(val / offsetInterval) * offsetInterval;
            return val;
        }
        private static float AngleSnap(float val)
        {
            if (GameSettings.VAB_USE_ANGLE_SNAP)
                return Mathf.Round(val / angleSnap) * angleSnap;
            return val;
        }
        private void Update()
        {
            if (!HighLogic.LoadedSceneIsEditor)
            {
                // gameObject.SetActive(false);
                return;
            }

            fineTune = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            angleSnap = fineTune ? EditorLogic.fetch.srfAttachAngleSnapFine : EditorLogic.fetch.srfAttachAngleSnap;
            offsetInterval = fineTune ? .125f : .25f;
        }
        void LateUpdate()
        {
            if (!gizmoEditingPart)
            {
                Deactivate();
                return;
            }
            if (!EditorToolActivated || !partSection0) return;
            var b0 = Vector3.Dot(EditorCamera.transform.forward, partSection0.transform.up) < .1f;
            var b1 = Vector3.Dot(EditorCamera.transform.forward, partSection1.transform.up) < .1f;
            if (!sec0FacingUser.HasValue || b0 != sec0FacingUser.Value)
            {
                sec0FacingUser = b0;
                for (int i = 0; i < partSection0.transform.childCount; i++)
                {
                    var h0 = partSection0.transform.GetChild(i).GetComponent<HandleGizmo>();
                    h0.hidden = !sec0FacingUser.Value;
                }
            }
            if (!sec1FacingUser.HasValue || b1 != sec1FacingUser.Value)
            {
                sec1FacingUser = b1;
                for (int i = 0; i < partSection1.transform.childCount; i++)
                {
                    var h1 = partSection1.transform.GetChild(i).GetComponent<HandleGizmo>();
                    h1.hidden = !sec1FacingUser.Value;
                }
            }
            if (!mouseDragging && !CVSPUIManager.Instance.MouseOverUI)
            {
                if (Input.GetKeyDown(KeyCode.Mouse0))
                {
                    Ray r;
                    r = EditorCamera.ScreenPointToRay(Input.mousePosition);
                    if (Physics.Raycast(r, out RaycastHit hit, 100f, 1 << GizmosLayer))
                    {
                        var go = hit.collider.transform;
                        if (go.parent.name.StartsWith("section"))
                        {
                            currentHandle = go.GetComponent<HandleGizmo>();
                            currentHandle.OnClick();
                            lastTwist = gizmoEditingPart.Twist;
                            mouseDragging = true;
                        }
                    }
                    else
                    {
                        // if (!mouseOverUI)//没有拖动变形手柄，且按下左键，则隐藏变形手柄，隐藏编辑工具
                        Deactivate();
                    }
                }
                else if (Input.GetKeyDown(KeyCode.Escape)) Deactivate();
                else if (Input.GetKey(KeyCode.Keypad1))
                {
                    Run -= .025f;
                    Run = Mathf.Clamp(Run, -CVSPConfigs.MaxSize, CVSPConfigs.MaxSize);
                    gizmoEditingPart.Run = OffsetSnap(Run);
                    goto IL_1;
                }
                else if (Input.GetKey(KeyCode.Keypad7))
                {
                    Run += .025f;
                    Run = Mathf.Clamp(Run, -CVSPConfigs.MaxSize, CVSPConfigs.MaxSize);
                    gizmoEditingPart.Run = OffsetSnap(Run);
                    goto IL_1;
                }
                else if (Input.GetKey(KeyCode.Keypad3))
                {
                    Raise -= .025f;
                    Raise = Mathf.Clamp(Raise, -CVSPConfigs.MaxSize, CVSPConfigs.MaxSize);
                    gizmoEditingPart.Raise = OffsetSnap(Raise);
                    goto IL_1;
                }
                else if (Input.GetKey(KeyCode.Keypad9))
                {
                    Raise += .025f;
                    Raise = Mathf.Clamp(Raise, -CVSPConfigs.MaxSize, CVSPConfigs.MaxSize);
                    gizmoEditingPart.Raise = OffsetSnap(Raise);
                    goto IL_1;
                }
                goto IL_2;
            IL_1:
                gizmoEditingPart.OnEditorMouseRelease();
            }
            else if (mouseDragging || gizmoEditingPart.PartParamModified)
            {
                CalcGizmosSize();
                if (Input.GetKeyUp(KeyCode.Mouse0))
                {
                    mouseDragging = false;
                    gizmoEditingPart.OnEditorMouseRelease();
                    if (currentHandle != null)
                        currentHandle.OnRelease();
                }
            }
        IL_2:
            return;
        }
        private void CreateGizmos()
        {
            if (prefab != null)
            {
                if ((section0 == null || section1 == null))
                {
                    // Debug.Log("[CRFP] Added Editor Gizmos");
                    section0 = Instantiate<GameObject>(prefab);
                    section0.name = "section0";
                    section0.transform.SetParent(_gameObject.transform, false);
                    section1 = Instantiate<GameObject>(prefab);
                    section1.name = "section1";
                    section1.transform.SetParent(_gameObject.transform, false);
                }
            }
        }
        private void SetupGizmos()
        {
            if (prefab != null)
            {
                if ((partSection0 == null || partSection1 == null))
                {
                    //  Debug.Log("[CRFP] Added Editor Gizmos");
                    partSection0 = gizmoEditingPart.Section0Transform.gameObject;
                    partSection1 = gizmoEditingPart.Section1Transform.gameObject;
                    while (0 < section0.transform.childCount)
                    {
                        var t = section0.transform.GetChild(0);
                        t.SetParent(partSection0.transform, false);
                    }
                    while (0 < section1.transform.childCount)
                    {
                        var t = section1.transform.GetChild(0);
                        t.SetParent(partSection1.transform, false);
                    }
                    RegisterEvents(partSection0);
                    RegisterEvents(partSection1);
                    //throw new Exception();
                }
            }
        }
        private void CalcGizmosSize()
        {
            float witdth0, witdth1;
            float height1, height0;
            witdth0 = Mathf.Max(MinScale, gizmoEditingPart.Section0Width);
            witdth1 = Mathf.Max(MinScale, gizmoEditingPart.Section1Width);
            height0 = Mathf.Max(MinScale, gizmoEditingPart.Section0Height);
            height1 = Mathf.Max(MinScale, gizmoEditingPart.Section1Height);

            var scale0 = Mathf.Min(MaxScale, Mathf.Min(witdth0, height0) * .5f);
            var scale1 = Mathf.Min(MaxScale, Mathf.Min(witdth1, height1) * .5f);
            for (int i = 0; i < partSection0.transform.childCount; i++)
            {
                Transform t = partSection0.transform.GetChild(i);
                t.localPosition = new Vector3(Sign(t.localPosition.x) * witdth0 / 2, 0, Sign(t.localPosition.z) * height0 / 2);
                t.localScale = Vector3.one * scale0;
            }
            for (int i = 0; i < partSection1.transform.childCount; i++)
            {
                Transform t = partSection1.transform.GetChild(i);
                t.localPosition = new Vector3(Sign(t.localPosition.x) * witdth1 / 2, 0, Sign(t.localPosition.z) * height1 / 2);
                t.localScale = Vector3.one * scale1;
            }
        }
        private static float Sign(float a)
        {
            return IsZero(a) ? 0 : (a > 0 ? 1 : -1);
        }
        private static bool IsZero(float a)
        {
            return Mathf.Abs(a) < 1e-4;
        }
        private void RegisterEvents(GameObject g)
        {
            var t = g.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform transform1 = t.GetChild(i);
                HandleGizmo handleGizmo = transform1.GetComponent<HandleGizmo>();
                if (handleGizmo) handleGizmo.OnValueChanged += OnGizmoEdit;
            }
        }
        private void RemoveEvents(GameObject g)
        {
            if (!g) return;
            var t = g.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                HandleGizmo handleGizmo = t.GetChild(i).GetComponent<HandleGizmo>();
                if (handleGizmo) handleGizmo.OnValueChanged -= OnGizmoEdit;
            }
        }
        private void SpawnCVSP(CVSPPartInfo info)
        {
            if (ModuleCarnationVariablePart.PartDragging)
            {
                string s = "#LOC_CVSP_WRN_PartDragging";
                Localizer.TryGetStringByTag(s, out s);
                ScreenMessages.PostScreenMessage(s, 2f, ScreenMessageStyle.UPPER_RIGHT, Color.magenta);
                return;
            }

            CVSPInfoSpawned = info;

            GameEvents.onEditorPartEvent.Add(CaptureCreatedPart);

            EditorLogic.fetch.SpawnPart(ModuleCarnationVariablePart.partInfo);

            GameEvents.onEditorPartEvent.Remove(CaptureCreatedPart);

            WaitingForPartAttach = true;
        }
        void CaptureCreatedPart(ConstructionEventType type, Part p)
        {
            //if (type == ConstructionEventType.PartCreated)
            {
                p.attPos0 = Vector3.zero;
                p.attRotation = Quaternion.identity;
                p.transform.rotation = p.initRotation;
                var c = p.FindModuleImplementing<ModuleCarnationVariablePart>();
                c.Section0Width = CVSPInfoSpawned.width;
                c.Section1Width = CVSPInfoSpawned.width;
                c.Section0Height = CVSPInfoSpawned.height0;
                c.Section1Height = CVSPInfoSpawned.height1;
                c.Length = CVSPInfoSpawned.length;
                c.Twist = CVSPInfoSpawned.twist;
                c.Tilt0 = CVSPInfoSpawned.tilt0;
                c.Tilt1 = CVSPInfoSpawned.tilt1;
                c.Section0Radius = Vector4.zero;
                c.Section1Radius = Vector4.zero;
                c.UseEndsTexture = false;
                c.UseSideTexture = false;
                c.tankType = CreatorDefaultTankType;
                CVSPPartSpawned = p;
            }
        }
    }
}