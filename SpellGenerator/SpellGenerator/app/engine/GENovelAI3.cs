using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.IO;
using SpellGenerator.app.file;
using System.IO.Compression;

namespace SpellGenerator.app.engine
{
    public class GENovalAI3 : GenerateEngine
    {
        private HttpClient httpClient = new HttpClient();
        private bool interrupted = false;


        private List<SamplingMethod> samplingMethods = new List<SamplingMethod>(new SamplingMethod[]
        {
             SamplingMethod.k_euler,
             SamplingMethod.k_euler_a,
             SamplingMethod.k_dpmpp_2s_a,
             SamplingMethod.k_dpmpp_2m,
             SamplingMethod.k_dpmpp_sde,
             SamplingMethod.ddim_v3
        });

        public override List<SamplingMethod> GetSamplingMethods()
        {
            return samplingMethods;
        }

        public override string GetEngineName()
        {
            return "Noval AI V3";
        }


        public GENovalAI3()
        {
            httpClient.Timeout = TimeSpan.FromMinutes(60);
        }

        public override void Txt2Img(GenConfig genConfig, int batchSize, int batchCount, string positivePrompt, string negativePrompt)
        {
            _ = Txt2ImgWebPost(genConfig, batchSize, batchCount, positivePrompt, negativePrompt);

        }

        public override void Img2Img(GenConfig genConfig, GenImageInfo refImageInfo, int batchSize, int batchCount, string positivePrompt, string negativePrompt)
        {
            _ = Img2ImgWebPost(genConfig, refImageInfo, batchSize, batchCount, positivePrompt, negativePrompt);
        }

        void MessageConnectError()
        {
            MessageBox.Show("连接NovelAI后台错误，请确认NovelAI是否可连接及token是否填写正确。");
        }

        void MessageNovelAI()
        {
            MessageBox.Show("NovelAI不支持该项操作。");
        }

        string GetApiBase()
        {
            return "https://"+ host +"/";
        }

        string GetApiApiBase()
        {
            return "https://api.novelai.net/";
        }

        async Task<dynamic> CallApiGet(string api, object data)
        {
            string json = "";
            if(data != null)
            {
                json = JsonConvert.SerializeObject(data);
            }

            HttpContent httpContent = new StringContent(json, Encoding.UTF8, "application/json");
            HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, new Uri(GetApiApiBase() + api));
            httpRequestMessage.Content = httpContent;

            httpRequestMessage.Headers.Add("Authorization", "Bearer "+token);
            httpRequestMessage.Headers.Add("Origin", "https://novelai.net");
            httpRequestMessage.Headers.Add("Referer", "https://novelai.net/");
            System.Diagnostics.Debug.WriteLine(httpRequestMessage.ToString());
            var response = await httpClient.SendAsync(httpRequestMessage, HttpCompletionOption.ResponseHeadersRead);
            var content = await response.Content.ReadAsStringAsync();
            System.Diagnostics.Debug.WriteLine(content);
            var resultData = JsonConvert.DeserializeObject<dynamic>(content);
            return resultData;
        }

        async Task<MemoryStream> GenApi(object data)
        {
            string json = "";
            if (data != null)
            {
                json = JsonConvert.SerializeObject(data);
            }

            HttpContent httpContent = new StringContent(json, Encoding.UTF8, "application/json");
            HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, new Uri(GetApiBase() + "ai/generate-image"));
            httpRequestMessage.Content = httpContent;

            httpRequestMessage.Headers.Add("Authorization", "Bearer " + token);
            httpRequestMessage.Headers.Add("Origin", "https://novelai.net");
            httpRequestMessage.Headers.Add("Referer", "https://novelai.net/");
            System.Diagnostics.Debug.WriteLine(httpRequestMessage.ToString());
            var response = await httpClient.SendAsync(httpRequestMessage, HttpCompletionOption.ResponseHeadersRead);
            var stream = await response.Content.ReadAsStreamAsync();

            MemoryStream memoryStream = null;
            using (ZipArchive archive = new ZipArchive(stream))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    using (Stream fileStream = entry.Open())
                    {
                        memoryStream = new MemoryStream();
                        fileStream.CopyTo(memoryStream);
                    }
                    break;
                }
            }
            return memoryStream;
        }


        public override async Task<List<GenImageInfo>> Txt2ImgGen(GenConfig genConfig, int batchSize, int batchCount, string positivePrompt, string negativePrompt)
        {
            long seed = genConfig.seed;
            if (seed == -1)
            {
                seed = new Random().Next();
            }
            string sampler = SamplingMethod.GetSamplingMethod(genConfig.samplingMethod).novelAIName;

            List<GenImageInfo> images = new List<GenImageInfo>();

            for (int i = 0; i < batchCount; i++)
            {

                var jsonObj = new
                {
                    action = "generate",
                    input = positivePrompt,
                    model = "nai-diffusion-3",
                    parameters = new
                    {
                        width = genConfig.width,
                        height = genConfig.height,
                        scale = genConfig.cfgScale,
                        sampler = sampler,
                        steps = genConfig.samplingSteps,
                        n_samples = 1,
                        ucPreset = 0,
                        add_original_image = false,
                        cfg_rescale = 0,
                        controlnet_strength = 1,
                        dynamic_thresholding = false,
                        legacy = false,
                        negative_prompt = negativePrompt,
                        noise_schedule = "native",
                        qualityToggle = true,
                        seed = seed,
                        sm = true,
                        sm_dyn = false,
                        uncond_scale = 1,
                        params_version = 1
                    }
                };

                MemoryStream memoryStream = await GenApi(jsonObj);
                if(memoryStream != null)
                {
                    SaveImage(memoryStream, seed, images);
                }

                seed++;

                (Application.Current.MainWindow as MainWindow).ProgressGen.Value = (i + 1)*100 / ((double)batchCount);
                if(interrupted)
                {
                    break;
                }
            }

            return images;
        }

        void SaveImage(MemoryStream memoryStream, long seed, List<GenImageInfo> images)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = memoryStream;
            bitmap.EndInit();
            GenImageInfo imageInfo = new GenImageInfo();
            imageInfo.seed = seed;
            imageInfo.imageData = memoryStream.ToArray();
            imageInfo.image = bitmap;
            imageInfo.imageType = "png";
            imageInfo.defaultFileName = System.DateTime.Now.ToString("yyyyMMddHHmmss"+ "_" + seed);
            images.Add(imageInfo);

            try
            {
                string path = AppCore.Instance.basePath;
                if (!Directory.Exists(Path.Combine(path, "history")))
                {
                    Directory.CreateDirectory(Path.Combine(path, "history"));
                }
                string fileName = Path.Combine(path, "history", imageInfo.defaultFileName + ".png");
                using (FileStream fs = File.Open(fileName, FileMode.Create))
                {
                    fs.Write(imageInfo.imageData, 0, imageInfo.imageData.Length);
                }
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex.ToString()); }
        }

        async Task CheckBase()
        {
            await CallApiGet("user/data", null);
        }

        bool CheckCostPoint(GenConfig genConfig)
        {
            if(genConfig.samplingSteps > 28)
            {
                if (MessageBox.Show("步数大于28，将会扣取点数(扣取数量以NovelAI为准)，是否继续？", "", MessageBoxButton.OKCancel) != MessageBoxResult.OK)
                {
                    return false;
                }
            }
            if (genConfig.width * (long)genConfig.height > 1048576l)
            {
                if (MessageBox.Show("分辨率较大，将会扣取点数(扣取数量以NovelAI为准)，是否继续？", "", MessageBoxButton.OKCancel) != MessageBoxResult.OK)
                {
                    return false;
                }
            }
            return true;
        }

        public async Task Txt2ImgWebPost(GenConfig genConfig, int batchSize, int batchCount, string positivePrompt, string negativePrompt)
        {
            try
            {
                if(!CheckCostPoint(genConfig))
                {
                    GenerateEnd();
                    return;
                }
                interrupted = false;
                await CheckBase();

                (Application.Current.MainWindow as MainWindow).ProgressGen.Visibility = Visibility.Visible;
                (Application.Current.MainWindow as MainWindow).ProgressGen.Value = 0;

                List<GenImageInfo> images = await Txt2ImgGen(genConfig, batchSize, batchCount, positivePrompt, negativePrompt);

                AppCore.Instance.DoneGenerate(images);

            }
            catch (Exception ex)
            {
                GenerateEnd();
                System.Diagnostics.Trace.WriteLine(ex);
                (Application.Current.MainWindow as MainWindow).ProgressGen.Visibility = Visibility.Hidden;
                MessageConnectError();
            }


        }

        public override async Task<List<GenImageInfo>> Img2ImgGen(GenConfig genConfig, GenImageInfo refImageInfo, int batchSize, int batchCount, string positivePrompt, string negativePrompt)
        {
            long seed = genConfig.seed;
            if (seed == -1)
            {
                seed = new Random().Next();
            }
            string sampler = SamplingMethod.GetSamplingMethod(genConfig.samplingMethod).novelAIName;
            string srcImg = Convert.ToBase64String(refImageInfo.imageData);

            List<GenImageInfo> images = new List<GenImageInfo>();

            for (int i = 0; i < batchCount; i++)
            {

                var jsonObj = new
                {
                    action = "img2img",
                    input = positivePrompt,
                    model = "nai-diffusion-3",
                    parameters = new
                    {
                        width = genConfig.width,
                        height = genConfig.height,
                        scale = genConfig.cfgScale,
                        sampler = sampler,
                        steps = genConfig.samplingSteps,
                        n_samples = 1,
                        ucPreset = 0,
                        add_original_image = false,
                        cfg_rescale = 0,
                        controlnet_strength = 1,
                        dynamic_thresholding = false,
                        legacy = false,
                        negative_prompt = negativePrompt,
                        noise_schedule = "native",
                        qualityToggle = true,
                        seed = seed,
                        extra_noise_seed = seed,
                        image = srcImg,
                        strength = genConfig.denoisingStrength,                    
                        sm = true,
                        sm_dyn = false,
                        uncond_scale = 1,
                        params_version = 1
                    }
                };

                MemoryStream memoryStream = await GenApi(jsonObj);
                if (memoryStream != null)
                {
                    SaveImage(memoryStream, seed, images);
                }

                seed++;

                (Application.Current.MainWindow as MainWindow).ProgressGen.Value = (i + 1) * 100 / ((double)batchCount);
                if (interrupted)
                {
                    break;
                }
            }

            return images;

        }

        public void GenerateEnd()
        {
            AppCore.Instance.generating = false;
            (Application.Current.MainWindow as MainWindow).ProgressGen.Visibility = Visibility.Hidden;


            (Application.Current.MainWindow as MainWindow).ButtonGenerate.Visibility = Visibility.Visible;
            (Application.Current.MainWindow as MainWindow).ButtonInterrupt.Visibility = Visibility.Collapsed;
        }

        public async Task Img2ImgWebPost(GenConfig genConfig, GenImageInfo refImageInfo, int batchSize, int batchCount, string positivePrompt, string negativePrompt)
        {
            try
            {
                if (!CheckCostPoint(genConfig))
                {
                    GenerateEnd();
                    return;
                }
                interrupted = false;
                await CheckBase();

                (Application.Current.MainWindow as MainWindow).ProgressGen.Visibility = Visibility.Visible;
                (Application.Current.MainWindow as MainWindow).ProgressGen.Value = 0;

                List<GenImageInfo> images = await Img2ImgGen(genConfig, refImageInfo, batchSize, batchCount, positivePrompt, negativePrompt);

                AppCore.Instance.DoneGenerate(images);


            }
            catch (Exception ex)
            {
                GenerateEnd();
                System.Diagnostics.Trace.WriteLine(ex);
                (Application.Current.MainWindow as MainWindow).ProgressGen.Visibility = Visibility.Hidden;
            }


        }

        public override void FetchModels()
        {
            MessageNovelAI();
        }

        public override void FetchExtraModels()
        {
            MessageNovelAI();
        }

        public override void Txt2ImgInterrupt()
        {
            interrupted = true;
        }

        public override void Img2ImgInterrupt()
        {
            interrupted = true;
        }

        public override void ChooseModel(string modelName)
        {
            MessageNovelAI();
            (Application.Current.MainWindow as MainWindow).EnableOperations();
        }

        public override void DeepDanbooru(GenImageInfo refImageInfo)
        {
            MessageNovelAI();
            (Application.Current.MainWindow as MainWindow).ButtonToTags.IsEnabled = true;
        }

        public override bool IsBaseDataLoaded()
        {
            return true;
        }

        public override async Task LoadBaseData()
        {

        }

        public override bool CanChooseModel()
        {
            return false;
        }

        public async override Task ChooseModelDo(string modelName)
        {

        }

        public override bool CanHighResFix()
        {
            return false;
        }

        public override List<string> GetUpscalers()
        {
            return new List<string>();
        }

        public override List<string> GetModels()
        {
            return new List<string>();
        }
        public override string GetCurrentModel()
        {
            return null;
        }
        public override List<string> GetVaes()
        {
            return new List<string>();
        }
    }
}
