// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using Android.Content;
using Android.Media;
using Android.Widget;
using Google.AR.Core;
using Google.AR.Sceneform;
using Google.AR.Sceneform.Assets;
using Google.AR.Sceneform.Math;
using Google.AR.Sceneform.Rendering;
using Google.AR.Sceneform.UX;
using Java.IO;
using Java.Util.Concurrent;
using Java.Util.Functions;
using Microsoft.Azure.SpatialAnchors;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Xamarin.Essentials;
using static Android.Graphics.ColorSpace;

namespace NeudesicIC
{
    public class ModelRenderableLoadedCallback : Java.Lang.Object, IConsumer
    {
        //private AzureSpatialAnchorsCoarseRelocActivity activity;
        private ModelRenderable modelRenderable;
        
        public ModelRenderableLoadedCallback(ModelRenderable modelRenderable)
        {
           
            this.modelRenderable = modelRenderable;
        }

        public void Accept(Java.Lang.Object material)
        {
            this.modelRenderable.Material = (Material)material;
        }
    }

    public class BuildMaterialCacheCallback : Java.Lang.Object, IConsumer
    {

        private TransformableNode node;
        private int key;
        public BuildMaterialCacheCallback(TransformableNode node, int key)
        {

            this.key = key;
            this.node = node;
        }

        public void Accept(Java.Lang.Object material)
        {
            AnchorVisual.solidColorInitialMaterialCache[this.key] = (Material)material;
        }
    }

    public class ModelMaterialLoadedCallback : Java.Lang.Object, IConsumer
    {
        
        private TransformableNode node;
        private int key;
        public ModelMaterialLoadedCallback(TransformableNode node,int key)
        {

            this.key = key;
            this.node = node;
        }

        public void Accept(Java.Lang.Object material)
        {
            AnchorVisual.solidColorModelMaterialCache[this.key] = (Material)material;
            this.node.Renderable.SetMaterial(key, (Material)material);
        }
    }

    public class AnchorVisual
    {
        public enum NamedShape
        {
            Sphere,
            Cube,
            Cylinder,
        }

        private TransformableNode transformableTextNodeNeu;
        private TransformableNode transformableNode;
        private TransformableNode transformableTextNodeDep;
        private TransformableNode transformableTextNodeGas;
        private TransformableNode transformableTextNodePre;
        private TransformableNode transformableTextNodeFlo;
        private TransformableNode transformableGifNode;

        private NamedShape shape = NamedShape.Sphere;
        private Material material;
        private ArFragment fragment;
        public List<int> partNumbers = new List<int>() { 0, 8, 9, 17 };
        private static Dictionary<int, CompletableFuture> solidColorMaterialCache = new Dictionary<int, CompletableFuture>();
        public static Dictionary<int, Material> solidColorModelMaterialCache = new Dictionary<int, Material>();
        public static Dictionary<int, Material> solidColorInitialMaterialCache = new Dictionary<int, Material>();
        public event EventHandler<bool> ModelLoaded;
        public MediaPlayer mediaPlayer;
        List<DeviceTelemetry> deviceTelemetries;
        public string SelectedModel;
        public bool IsModelLoaded = false;
        public AnchorVisual(ArFragment arFragment, Anchor localAnchor , string selectedModel = null)
        {
            this.fragment = arFragment;
            AnchorNode = new AnchorNode(localAnchor);

            transformableNode = new TransformableNode(arFragment.TransformationSystem);
            transformableNode.ScaleController.Enabled = true;
            SelectedModel = selectedModel;           
            if (selectedModel == "cement")
            {
                transformableNode.ScaleController.MinScale = 0.2f;
                transformableNode.ScaleController.MaxScale = 2.0f;
                transformableNode.ScaleController.TransformableNode.LocalScale = new Vector3(0.3f, 0.3f, 0.3f);
                transformableNode.LocalPosition = new Vector3(0.0f, 0.5f, 0.0f);
            }
            else
            {
                transformableNode.ScaleController.MinScale = 0.3f;
                transformableNode.ScaleController.MaxScale = 0.7f;
                transformableNode.ScaleController.TransformableNode.LocalScale = new Vector3(0.5f, 0.5f, 0.5f);
            }
            transformableNode.TranslationController.Dispose();
            transformableNode.RotationController.Enabled = false;
            transformableNode.SetParent(AnchorNode);

            transformableGifNode = new TransformableNode(arFragment.TransformationSystem);
            transformableGifNode.TranslationController.Enabled = true;
            transformableGifNode.TranslationController.TransformableNode.LocalPosition = new Vector3(0.0f, 0.8f, 0.3f);
            transformableGifNode.ScaleController.Enabled = true;
            transformableGifNode.ScaleController.MinScale = 0.1f;
            transformableGifNode.ScaleController.MaxScale = 0.4f;
            transformableGifNode.ScaleController.TransformableNode.LocalScale = new Vector3(0.1f, 0.1f, 0.1f);
            transformableGifNode.SetParent(AnchorNode);
            transformableNode.AddChild(transformableGifNode);

            this.mediaPlayer = MediaPlayer.Create(this.fragment.Context, Resource.Raw.hazard_alarm);

            AddDepthTelimetryNode(this.fragment);
            AddGasdetectionTelimetryNode(this.fragment);
            AddPressureTelimetryNode(this.fragment);
            AddFlowTelimetryNode(this.fragment);
            NeudesicICDemoNode(this.fragment);
        }

        public AnchorVisual(ArFragment arFragment, CloudSpatialAnchor cloudAnchor)
            : this(arFragment, cloudAnchor.LocalAnchor , string.Empty)
        {
            this.fragment = arFragment;
            CloudAnchor = cloudAnchor;
        }

        public AnchorNode AnchorNode { get; }

        public CloudSpatialAnchor CloudAnchor { get; set; }

        public Anchor LocalAnchor => this.AnchorNode.Anchor;

        public NamedShape Shape
        {
            get { return shape; }
            set
            {
                if (shape != value)
                {
                    shape = value;
                    MainThread.BeginInvokeOnMainThread(RecreateRenderableOnMainThread);
                }
            }
        }

        public bool IsMovable
        {
            set
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    transformableNode.TranslationController.Enabled = value;
                    transformableNode.RotationController.Enabled = value;
                });
            }
        }

        public void AddToScene(ArFragment arFragment)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                RecreateRenderableOnMainThread();
                AnchorNode.SetParent(arFragment.ArSceneView.Scene);
            });
        }

        public void SetColor(Context context, int rgb)
        {
            lock (this)
            {
                if (!solidColorMaterialCache.ContainsKey(rgb))
                {
                    solidColorMaterialCache[rgb] = MaterialFactory.MakeOpaqueWithColor(context, new Color(rgb));
                }
                CompletableFuture loadMaterial = solidColorMaterialCache[rgb];
                loadMaterial.ThenAccept(new FutureResultConsumer<Material>(SetMaterial));
            }
        }

        public void SetModelColor(Context context, int rgb)
        {
            lock (this)
            {
                if (!solidColorMaterialCache.ContainsKey(rgb))
                {
                    solidColorMaterialCache[rgb] = MaterialFactory.MakeOpaqueWithColor(context, new Color(rgb));
                }
                CompletableFuture loadMaterial = solidColorMaterialCache[rgb];
                loadMaterial.ThenAccept(new FutureResultConsumer<Material>(SetModelMaterial));
            }
        }

        public void SetModelColor(Context context, int rgb, int key)
        {
            lock (this)
            {                
                MaterialFactory.MakeOpaqueWithColor(context, new Color(rgb)).ThenAccept(new ModelMaterialLoadedCallback(this.transformableNode, key));
            }
        }

        public void SetModelMaterial(Material material)
        {
            if (this.material != material)
            {
                this.material = material;                
            }
        }

        public void SetMaterial(Material material)
        {
            if (this.material != material)
            {
                this.material = material;
                MainThread.BeginInvokeOnMainThread(RecreateRenderableOnMainThread);
            }
        }

        public void Destroy()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                AnchorNode.Renderable = null;
            AnchorNode.SetParent(null);
            Anchor localAnchor = AnchorNode.Anchor;
            if (localAnchor != null)
            {
                    AnchorNode.Anchor = null;
                    localAnchor.Detach();
                }
            });
        }

        private void RecreateRenderableOnMainThread()
        {
            if (material != null)
            {
                LoadGlb();
                //LoadText(); 
            }
        }

        private void FinishLoading(ModelRenderable model)
        {       
            transformableNode.Renderable = model;
            //MaterialFactory.MakeOpaqueWithColor(this.fragment.Context, new Color(Android.Graphics.Color.Red))
            //         .ThenAccept(new ModelRenderableLoadedCallback((ModelRenderable)transformableNode.Renderable));
            //SetColor(this.fragment.Context, Android.Graphics.Color.Red);
            //ApplyColorToChildNodes(transformableNode, Android.Graphics.Color.Red);
            //LoadTelemetry();
            // this.StartPlayer("//android_asset/hazard-alarm.mp3");
           
            this.ModelLoaded?.Invoke(this, true);
        }

        private void ApplyColorToChildNodes(Node parentNode, int rgb)
        {
            Random randomGenerator = new Random();
     
            foreach(var partNumber in partNumbers)
            {
                SetModelColor(this.fragment.Context, Android.Graphics.Color.Green, partNumber);             
            }
                            
            //for(int i = 0; i < transformableNode.Renderable.SubmeshCount; i++)
            //{
            //    transformableNode.Renderable.SetMaterial(i, solidColorModelMaterialCache[i]);
            //}
            //foreach (Node childNode in parentNode.Children)
            //{
            //    if (childNode.Renderable is ModelRenderable)
            //    {
            //        // Create a new material with the specified color
            //        MaterialFactory.MakeOpaqueWithColor(this.fragment.Context, new Color(rgb))
            //           .ThenAccept(new ModelRenderableLoadedCallback((ModelRenderable)childNode.Renderable));
            //    }
            

            //    // Recursive call to apply color to nested child nodes
            //    ApplyColorToChildNodes(childNode, rgb);
            //}
        }

        private void FinishLoadingGIF(ViewRenderable model)
        {
            transformableGifNode.Renderable = model;

        }

        public void Fetch_LoadTelemetry()
        {
            MainThread.BeginInvokeOnMainThread(LoadTelemetry);
        }

        private void LoadTelemetry()
        {
            using (HttpClient httpClient = new HttpClient())
            {
                try
                {
                    string url = "https://dt-twin-api.azurewebsites.net/api/GetTelimetryData?"; // Example API endpoint

                    HttpResponseMessage response = httpClient.GetAsync(url).Result;

                    if (response.IsSuccessStatusCode)
                    {
                        string content =  response.Content.ReadAsStringAsync().Result;
                        System.Console.WriteLine(content);
                        deviceTelemetries = JsonConvert.DeserializeObject<List<DeviceTelemetry>>(content);
                        bool isAnomalyFound = deviceTelemetries.Any((x) => x.ColorValue != 0);
                        foreach (var deviceTelemetry in deviceTelemetries)
                        {
                            if (deviceTelemetry.Id == "kb1.001.depth")
                            {
                                SetModelColor(this.fragment.Context, GetColorValue(deviceTelemetry.ColorValue), 0);
                                UpdateTelemetryValue(deviceTelemetry, 0);
                                //this.transformableNode.Renderable.SetMaterial(0, solidColorInitialMaterialCache[GetColorValue(deviceTelemetry.ColorValue)]);
                            }
                            else if (deviceTelemetry.Id == "kb1.001.gasdetection")
                            {
                                SetModelColor(this.fragment.Context, GetColorValue(deviceTelemetry.ColorValue), 9);
                                UpdateTelemetryValue(deviceTelemetry, 9);
                            }
                            else if (deviceTelemetry.Id == "kb1.001.pressure")
                            {
                                SetModelColor(this.fragment.Context, GetColorValue(deviceTelemetry.ColorValue), 8);
                                UpdateTelemetryValue(deviceTelemetry, 8);
                            }
                            else if (deviceTelemetry.Id == "kb1.001.flowin")
                            {
                                SetModelColor(this.fragment.Context, GetColorValue(deviceTelemetry.ColorValue), 10);
                                UpdateTelemetryValue(deviceTelemetry, 10);
                            }
                        }

                        if (isAnomalyFound)
                        {
                            this.PlayAudio();
                            LoadHazardGIF();
                        }
                        else
                        {
                            this.StopAudio();
                            UnLoadHazardGIF();
                        }

                        if (IsModelLoaded == false)
                        {
                            LoadTelemetryValue();
                        }
                    }
                    else
                    {
                        System.Console.WriteLine($"HTTP request failed with status code: {response.StatusCode}");
                    }
                }
                catch (System.Exception ex)
                {
                    System.Console.WriteLine($"An error occurred: {ex.Message}");
                }
            }
        }

        private void LoadGlb()
        {
            var uri = SelectedModel == "cement" ? Android.Net.Uri.FromFile(new File("//android_asset/cementfactory.glb"))
                : Android.Net.Uri.FromFile(new File("//android_asset/oil.glb"));

            Render3DModel(uri);

        }

        private void Render3DModel(Android.Net.Uri uri)
        {
            var builder = ModelRenderable.InvokeBuilder();
            var javaClass = Java.Lang.Class.FromType(builder.GetType());
            var methods = javaClass.GetMethods();
            var model = RenderableSource
                .InvokeBuilder()
                .SetSource(this.fragment.Context, uri, RenderableSource.SourceType.Glb)
                //.SetRecenterMode(RenderableSource.RecenterMode.Center)
                // Or .gltf              
                .Build();
            var method = methods[13];
            method.Invoke(builder, this.fragment.Context, model);
            builder.Build(FinishLoading);
        }

        private void NeudesicICDemoNode(ArFragment arFragment)
        {
            transformableTextNodeNeu = new TransformableNode(arFragment.TransformationSystem);
            transformableTextNodeNeu.TranslationController.Enabled = true;
            transformableTextNodeNeu.TranslationController.TransformableNode.LocalPosition = new Vector3(0.0f, 0.5f, 0.3f);
            transformableTextNodeNeu.ScaleController.Enabled = true;
            transformableTextNodeNeu.ScaleController.TransformableNode.LocalScale = new Vector3(3.0f, 3.0f, 3.0f);
            transformableTextNodeNeu.SetParent(AnchorNode);

            //var builder_N = ViewRenderable.InvokeBuilder().SetView(this.fragment.Context, Resource.Layout.textElement);
            //builder_N.Build(FinishLoadingText_NeudesicText);
        }

        private void AddDepthTelimetryNode(ArFragment arFragment)
        {
            transformableTextNodeDep = new TransformableNode(arFragment.TransformationSystem);
            transformableTextNodeDep.TranslationController.Enabled = true;
            transformableTextNodeDep.TranslationController.TransformableNode.LocalPosition = new Vector3(0.7f, 0.6f, 0.0f);
            transformableTextNodeDep.ScaleController.Enabled = true;
            transformableTextNodeDep.ScaleController.TransformableNode.LocalScale = new Vector3(1.0f, 1.0f, 0.0f);
            transformableTextNodeDep.SetParent(AnchorNode);
        }

        private void AddGasdetectionTelimetryNode(ArFragment arFragment)
        {
            transformableTextNodeGas = new TransformableNode(arFragment.TransformationSystem);
            transformableTextNodeGas.TranslationController.Enabled = true;
            transformableTextNodeGas.TranslationController.TransformableNode.LocalPosition = new Vector3(0.9f, 2.4f, 0.0f);
            transformableTextNodeGas.ScaleController.Enabled = true;
            transformableTextNodeGas.ScaleController.TransformableNode.LocalScale = new Vector3(1.0f, 1.0f, 0.0f);
            transformableTextNodeGas.SetParent(AnchorNode);
        }

        private void AddPressureTelimetryNode(ArFragment arFragment)
        {
            transformableTextNodePre = new TransformableNode(arFragment.TransformationSystem);
            transformableTextNodePre.TranslationController.Enabled = true;
            transformableTextNodePre.TranslationController.TransformableNode.LocalPosition = new Vector3(0.8f, 2.6f, -0.2f);
            transformableTextNodePre.ScaleController.Enabled = true;
            transformableTextNodePre.ScaleController.TransformableNode.LocalScale = new Vector3(3.0f, 3.0f, 3.0f);
            transformableTextNodePre.SetParent(AnchorNode);
        }

        private void AddFlowTelimetryNode(ArFragment arFragment)
        {
            transformableTextNodeFlo = new TransformableNode(arFragment.TransformationSystem);
            transformableTextNodeFlo.TranslationController.Enabled = true;
            transformableTextNodeFlo.TranslationController.TransformableNode.LocalPosition = new Vector3(-0.5f, 2.5f, 0.12f);
            transformableTextNodeFlo.ScaleController.Enabled = true;
            transformableTextNodeFlo.ScaleController.TransformableNode.LocalScale = new Vector3(1.0f, 1.0f, 0.0f);
            transformableTextNodeFlo.SetParent(AnchorNode);
        }

        private void LoadTelemetryValue()
        {           
            IsModelLoaded = true;
            var builder_D = ViewRenderable.InvokeBuilder().SetView(this.fragment.Context, Resource.Layout.DepthTelemetry);
            builder_D.Build(FinishLoadingText_D);

            var builder_G = ViewRenderable.InvokeBuilder().SetView(this.fragment.Context, Resource.Layout.GasdetectionTelemetry);
            builder_G.Build(FinishLoadingText_G);

            var builder_P = ViewRenderable.InvokeBuilder().SetView(this.fragment.Context, Resource.Layout.PressureTelemetry);
            builder_P.Build(FinishLoadingText_P);

            var builder_F = ViewRenderable.InvokeBuilder().SetView(this.fragment.Context, Resource.Layout.FlowTelemetry);
            builder_F.Build(FinishLoadingText_F);
        }

        private void UpdateTelemetryValue(DeviceTelemetry telimetry, int key)
        {
            if (IsModelLoaded)
            {
                if (key == 0)
                {
                    var renderableView = (ViewRenderable)transformableTextNodeDep.Renderable;
                    var textView = (TextView)renderableView.View;
                    textView.SetText("Asset : kb1.001.depth" + "\n" + telimetry.Property + ":" + telimetry.Value.ToString(), TextView.BufferType.Normal);
                }

                if (key == 9)
                {
                    var renderableView = (ViewRenderable)transformableTextNodeGas.Renderable;
                    var textView = (TextView)renderableView.View;
                    textView.SetText("Asset : kb1.001.gasdetection" + "\n" + telimetry.Property + ":" + telimetry.Value.ToString(), TextView.BufferType.Normal);
                }

                if (key == 8)
                {
                    var renderableView = (ViewRenderable)transformableTextNodePre.Renderable;
                    var textView = (TextView)renderableView.View;
                    textView.SetText("Asset : kb1.001.pressure" + "\n" + telimetry.Property + ":" + telimetry.Value.ToString(), TextView.BufferType.Normal);
                }
                if (key == 10)
                {
                    var renderableView = (ViewRenderable)transformableTextNodeFlo.Renderable;
                    var textView = (TextView)renderableView.View;
                    textView.SetText("Asset : kb1.001.flowin" + "\n" + telimetry.Property + ":" + telimetry.Value.ToString(), TextView.BufferType.Normal);
                }
            }
        }
        private void FinishLoadingText_NeudesicText(ViewRenderable model)
        {
            transformableTextNodeNeu.Renderable = model;
            TextView textView = (TextView)model.View;
            textView.SetText("Neudesic IC Demo", TextView.BufferType.Normal);
            this.transformableNode.AddChild(transformableTextNodeNeu);
        }

        private void FinishLoadingText_D(ViewRenderable model)
        {
            var telimetry = deviceTelemetries.Where(k => k.Id == "kb1.001.depth").FirstOrDefault();
            if (telimetry != null)
            {
                transformableTextNodeDep.Renderable = model;              
                TextView textView = (TextView)model.View;
                textView.SetText("Asset : kb1.001.depth" + "\n" + telimetry.Property +":"+ telimetry.Value.ToString() , TextView.BufferType.Normal);
                textView.SetBackgroundResource(Resource.Drawable.yellowr);
                this.transformableNode.AddChild(transformableTextNodeDep);
            }
        }

        private void FinishLoadingText_G(ViewRenderable model)
        {
            var telimetry = deviceTelemetries.Where(k => k.Id == "kb1.001.gasdetection").FirstOrDefault();
            if (telimetry != null)
            {
                transformableTextNodeGas.Renderable = model;
                TextView textView = (TextView)model.View;
                textView.SetText("Asset : kb1.001.gasdetection" + "\n" + telimetry.Property + ":" + telimetry.Value.ToString(), TextView.BufferType.Normal);
                textView.SetBackgroundResource(Resource.Drawable.yellowr);
                this.transformableNode.AddChild(transformableTextNodeGas);
            }
        }

        private void FinishLoadingText_P(ViewRenderable model)
        {
            var telimetry = deviceTelemetries.Where(k => k.Id == "kb1.001.pressure").FirstOrDefault();
            if (telimetry != null)
            {
                transformableTextNodePre.Renderable = model;
                TextView textView = (TextView)model.View;
                textView.SetText("Asset : kb1.001.pressure" + "\n" + "pressure" + ":" + telimetry.Value.ToString(), TextView.BufferType.Normal);
                textView.SetBackgroundResource(Resource.Drawable.yellowr);
                this.transformableNode.AddChild(transformableTextNodePre);
            }
        }

        private void FinishLoadingText_F(ViewRenderable model)
        {
            var telimetry = deviceTelemetries.Where(k => k.Id == "kb1.001.flowin").FirstOrDefault();
            if (telimetry != null)
            {
                transformableTextNodeFlo.Renderable = model;
                TextView textView = (TextView)model.View;
                textView.SetText("Asset : kb1.001.flowin" + "\n" + telimetry.Property + ":" + telimetry.Value.ToString(), TextView.BufferType.Normal);
                textView.SetBackgroundResource(Resource.Drawable.bubble_yellow);
                this.transformableNode.AddChild(transformableTextNodeFlo);
            }
        }
        
        private void LoadHazardGIF()
        {
            var builder = ViewRenderable.InvokeBuilder().SetView(this.fragment.Context, Resource.Layout.hazardElement);

            builder.Build(FinishLoadingGIF);
        }

        private void UnLoadHazardGIF()
        {
            this.transformableGifNode.Renderable = null;
        }

        private int GetColorValue(int flagId)
        {
            if(flagId == 0)
            {
                return Android.Graphics.Color.Green;
            }
            else if(flagId == 1)
            {
                return 16760576;
            }
            else
            {
                return Android.Graphics.Color.Red;
            }
        }

        public void PlayAudio()
        {
            Intent serviceIntent = new Intent(this.fragment.Context, typeof(AudioService));
            serviceIntent.PutExtra("AudioUri", "filepath");
            this.fragment.Context.StartService(serviceIntent);
        }

        public void StopAudio()
        {
            Intent stopIntent = new Intent("StopAudioAction");
            this.fragment.Context.SendBroadcast(stopIntent);
        }

    }

    public class DeviceTelemetry
    {
        public string Id { get; set; }
        public int ColorValue { get; set; }
        public string Property { get; set; }
        public decimal Value { get; set; }
    }
}