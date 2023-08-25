// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.Media;
using Android.Net;
using Android.Support.V4.App;
using Android.Widget;
using Google.AR.Core;
using Google.AR.Sceneform;
using Google.AR.Sceneform.Assets;
using Google.AR.Sceneform.Math;
using Google.AR.Sceneform.Rendering;
using Google.AR.Sceneform.UX;
using Java.IO;
using Java.Lang;
using Java.Util.Concurrent;
using Java.Util.Functions;
using Microsoft.Azure.SpatialAnchors;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Android.Content.Res;
using static Android.Graphics.ColorSpace;
using System.Linq;

namespace SampleXamarin
{
    public class ModelRenderableLoadedCallback : Java.Lang.Object, IConsumer
    {
        private AzureSpatialAnchorsCoarseRelocActivity activity;
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

        private TransformableNode transformableNode;

        private TransformableNode transformableTextNode;
        private TransformableNode transformableGifNode;
        private NamedShape shape = NamedShape.Sphere;
        private Material material;
        private ArFragment fragment;
        private FragmentActivity activity;
        public List<int> partNumbers = new List<int>() { 0, 8, 9, 17 };
        private static Dictionary<int, CompletableFuture> solidColorMaterialCache = new Dictionary<int, CompletableFuture>();
        public static Dictionary<int, Material> solidColorModelMaterialCache = new Dictionary<int, Material>();
        public static Dictionary<int, Material> solidColorInitialMaterialCache = new Dictionary<int, Material>();
        public event EventHandler<bool> ModelLoaded;
        public MediaPlayer mediaPlayer;

        //public AnchorVisual(ArFragment arFragment, Anchor localAnchor, FragmentActivity activity)
        //{
        //    this.fragment=arFragment;
        //    AnchorNode = new AnchorNode(localAnchor);

        //    transformableNode = new TransformableNode(arFragment.TransformationSystem);
        //    transformableNode.ScaleController.Enabled = false;
        //    transformableNode.TranslationController.Enabled = false;
        //    transformableNode.RotationController.Enabled = false;
        //    transformableNode.SetParent(AnchorNode);
        //}

        public AnchorVisual(ArFragment arFragment, Anchor localAnchor)
        {
            this.fragment = arFragment;
            AnchorNode = new AnchorNode(localAnchor);
    
            transformableNode = new TransformableNode(arFragment.TransformationSystem);
            transformableNode.ScaleController.Enabled = true;
            transformableNode.ScaleController.MinScale = 0.3f;
            transformableNode.ScaleController.MaxScale = 0.7f;
            transformableNode.ScaleController.TransformableNode.LocalScale = new Vector3(0.5f, 0.5f, 0.5f);
            transformableNode.TranslationController.Dispose(); 
            transformableNode.RotationController.Enabled = false;
            transformableNode.SetParent(AnchorNode);

            
        


            transformableTextNode = new TransformableNode(arFragment.TransformationSystem);
            transformableTextNode.TranslationController.Enabled = true;
            transformableTextNode.TranslationController.TransformableNode.LocalPosition = new Vector3(0.0f, 0.5f, 0.3f);

            transformableTextNode.ScaleController.Enabled = true;
            transformableTextNode.ScaleController.TransformableNode.LocalScale = new Vector3(3.0f, 3.0f, 3.0f);
            //transformableTextNode.TranslationController.Enabled = false;
            //transformableTextNode.RotationController.Enabled = false;
            transformableTextNode.SetParent(AnchorNode);

            transformableGifNode = new TransformableNode(arFragment.TransformationSystem);
            transformableGifNode.TranslationController.Enabled = true;
            transformableGifNode.TranslationController.TransformableNode.LocalPosition = new Vector3(0.0f, 0.8f, 0.3f);
            transformableGifNode.ScaleController.Enabled = true;
            transformableGifNode.ScaleController.MinScale = 0.1f;
            transformableGifNode.ScaleController.MaxScale = 0.4f;
            transformableGifNode.ScaleController.TransformableNode.LocalScale = new Vector3(0.1f, 0.1f, 0.1f);
            transformableGifNode.SetParent(AnchorNode);

            this.mediaPlayer = MediaPlayer.Create(this.fragment.Context, Resource.Raw.hazard_alarm);

        }
        public AnchorVisual(ArFragment arFragment, CloudSpatialAnchor cloudAnchor)
            : this(arFragment, cloudAnchor.LocalAnchor)
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
                //if (!solidColorModelMaterialCache.ContainsKey(key))
                //{
                //    solidColorModelMaterialCache[key] = MaterialFactory.MakeOpaqueWithColor(context, new Color(rgb));
                //}
                //CompletableFuture loadMaterial = solidColorModelMaterialCache[key];
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
                LoadText();
                

                //string packageName = Application.Context.PackageName;
                //var builder = ModelRenderable.InvokeBuilder();
                //var javaClass = Java.Lang.Class.FromType(builder.GetType());
                //var methods = javaClass.GetMethods();
                //var method = methods[11]; // setSource metho
                //method.Invoke(builder, this.fragment.Context, Resource.Raw.andy);
                //builder.Build(FinishLoading);



                //        int resourceId = Resource.Raw.traffic_cones; // Replace with the resource ID of your file
                //        string packageName = Application.Context.PackageName;

                //        var model = RenderableSource
                //.InvokeBuilder()
                //.SetSource(this.fragment.Context, Android.Net.Uri.Parse($"android.resource://{packageName}/{resourceId}"), RenderableSource.SourceType.Glb) // Or .gltf
                //.Build();
                //        
                //        var method1 = methods[13];
                //        method1.Invoke(builder, this.fragment.Context, model);

                //switch (shape)
                //{
                //    case NamedShape.Sphere:
                //        renderable = ShapeFactory.MakeSphere(
                //                0.1f,
                //                new Vector3(0.0f, 0.1f, 0.0f),
                //                material);
                //        transformableNode.Renderable = renderable;
                //        break;
                //    case NamedShape.Cube:
                //        renderable = ShapeFactory.MakeCube(
                //                new Vector3(0.161f, 0.161f, 0.161f),
                //                new Vector3(0.0f, 0.0805f, 0.0f),
                //                material);
                //        transformableNode.Renderable = renderable;
                //        break;
                //    case NamedShape.Cylinder:
                //        //renderable = ShapeFactory.MakeCylinder(
                //        //        0.0874f,
                //        //        0.175f,
                //        //        new Vector3(0.0f, 0.0875f, 0.0f),
                //        //        material);
                //        //ModelRenderable.Builder builder = ModelRenderable.InvokeBuilder();
                //        //builder.SetSource(this.fragment.Activity, Resource.Raw.chair); // Replace 'your_model' with the actual resource ID of your SFB model
                //        //builder.Build().ThenAccept(new ModelRenderableLoadedCallback(transformableNode));

                //        //var builder = ModelRenderable.InvokeBuilder();
                //        //builder.SetSource(this.fragment.Context, Resource.Raw.chair);
                //        //builder.Build().ThenAccept(new FutureResultConsumer<ModelRenderable>(FinishLoading));
                //        var builder = ModelRenderable.InvokeBuilder();
                //        var javaClass = Java.Lang.Class.FromType(builder.GetType());
                //        var methods = javaClass.GetMethods();
                //        var method = methods[11]; // setSource method

                //        method.Invoke(builder, this.fragment.Context, Resource.Raw.andy);
                //        builder.Build(FinishLoading);
                //        break;
                //    default:
                //        throw new InvalidOperationException("Invalid shape");
                //}

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

        private void FinishLoadingText(ViewRenderable model)
        {
            transformableTextNode.Renderable = model;
            TextView textView = (TextView)model.View;
            textView.SetText(Resource.String.ModelText, TextView.BufferType.Normal);
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
                        List<DeviceTelemetry> deviceTelemetries = JsonConvert.DeserializeObject<List<DeviceTelemetry>>(content);
                        bool isAnomalyFound = deviceTelemetries.Any((x) => x.ColorValue != 0);
                        foreach (var deviceTelemetry in deviceTelemetries)
                        {
                            if (deviceTelemetry.Id == "kb1.001.depth")
                            {
                                SetModelColor(this.fragment.Context, GetColorValue(deviceTelemetry.ColorValue), 0);
                                //this.transformableNode.Renderable.SetMaterial(0, solidColorInitialMaterialCache[GetColorValue(deviceTelemetry.ColorValue)]);
                            }
                            else if (deviceTelemetry.Id == "kb1.001.gasdetection")
                            {
                                SetModelColor(this.fragment.Context, GetColorValue(deviceTelemetry.ColorValue), 9);

                            }
                            else if (deviceTelemetry.Id == "kb1.001.pressure")
                            {
                                SetModelColor(this.fragment.Context, GetColorValue(deviceTelemetry.ColorValue), 8);
                            }
                            else if (deviceTelemetry.Id == "kb1.001.flowin")
                            {
                                SetModelColor(this.fragment.Context, GetColorValue(deviceTelemetry.ColorValue), 17);
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
            
            var builder = ModelRenderable.InvokeBuilder();
            var javaClass = Java.Lang.Class.FromType(builder.GetType());
            var uri = Android.Net.Uri.FromFile(new File("//android_asset/oil.glb"));
            var methods = javaClass.GetMethods();
            var model = RenderableSource
                .InvokeBuilder()
                .SetSource(this.fragment.Context, uri, RenderableSource.SourceType.Glb)
                //.SetScale(0.05f)
                // Or .gltf              
                .Build();
            var method = methods[13];
            method.Invoke(builder, this.fragment.Context, model);
            builder.Build(FinishLoading);
        }

        private void LoadText()
        {
            var builder = ViewRenderable.InvokeBuilder().SetView(this.fragment.Context, Resource.Layout.textElement);

            builder.Build(FinishLoadingText);
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

    }
}