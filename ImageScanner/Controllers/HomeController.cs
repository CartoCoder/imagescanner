using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using ImageResizer;
using ImageScanner.Models;
using Microsoft.Azure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Threading.Tasks;
using System.IO;
using Microsoft.ProjectOxford.Vision;
using Microsoft.ProjectOxford.Vision.Contract;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;

namespace ImageScanner.Controllers
{
	public class HomeController : Controller
	{
		[HttpPost]
		public async Task<ActionResult> Upload(HttpPostedFileBase file)
		{
			if (file != null && file.ContentLength > 0)
			{
				// make sure user selected an image file
				if (!file.ContentType.StartsWith("image"))
				{
					TempData["Message"] = "Only image files may be uploaded";
				}
				else
				{
					// save original image in the 'images' container
					CloudStorageAccount account = CloudStorageAccount.Parse(CloudConfigurationManager.GetSetting("StorageConnectionString"));
					CloudBlobClient client = account.CreateCloudBlobClient();
					CloudBlobContainer container = client.GetContainerReference("images");
					CloudBlockBlob photo = container.GetBlockBlobReference(Path.GetFileName(file.FileName));

					await photo.UploadFromStreamAsync(file.InputStream);
					file.InputStream.Seek(0L, SeekOrigin.Begin);

					// generate a thumbnail and save to 'thumbnails' container
					using (var outputStream = new MemoryStream())
					{
						var settings = new ResizeSettings { MaxWidth = 192, Format = "png" };
						ImageBuilder.Current.Build(file.InputStream, outputStream, settings);
						outputStream.Seek(0L, SeekOrigin.Begin);
						container = client.GetContainerReference("thumbnails");
						CloudBlockBlob thumbnail = container.GetBlockBlobReference(Path.GetFileName(file.FileName));

						await thumbnail.UploadFromStreamAsync(outputStream);
					}

					// submit image to CV api
					//VisionServiceClient vision = new VisionServiceClient(CloudConfigurationManager.GetSetting("SubscriptionKey"));					
					VisionServiceClient vision = new VisionServiceClient("5ac0788c7d0949e1b1e6a59bce09da46", "https://eastus2.api.cognitive.microsoft.com/vision/v1.0");
					VisualFeature[] features = new VisualFeature[] { VisualFeature.Description };
					var photoString = photo.Uri.ToString();
					AnalysisResult result = await vision.AnalyzeImageAsync(photoString, features);
					
					// record the image description and tags in the blob metadata
					photo.Metadata.Add("Caption", result.Description.Captions[0].Text);

					for (int i = 0; i < result.Description.Tags.Length; i++)
					{
						string key = String.Format("Tag{0}", i);
						photo.Metadata.Add(key, result.Description.Tags[i]);
					}

					await photo.SetMetadataAsync();

				}
			}
			// redirect back to the index action to show the form again
			return RedirectToAction("Index");
		}





		public ActionResult Index()
		{
			// Pass a list of blob URIs in ViewBag
			CloudStorageAccount account = CloudStorageAccount.Parse(CloudConfigurationManager.GetSetting("StorageConnectionString"));
			CloudBlobClient client = account.CreateCloudBlobClient();
			CloudBlobContainer container = client.GetContainerReference("images");
			List<BlobInfo> blobs = new List<BlobInfo>();
			foreach (IListBlobItem item in container.ListBlobs())
			{
				var blob = item as CloudBlockBlob;

				if (blob != null)
				{
					blob.FetchAttributes(); // get the blob metadata
					var caption = blob.Metadata.ContainsKey("Caption") ? blob.Metadata["Caption"] : blob.Name;
					//var tags = blob.Metadata.ContainsKey("Tags") ? blob.Metadata["Tags"] : blob.Name;

					blobs.Add(new BlobInfo()
					{
						ImageUri = blob.Uri.ToString(),
						ThumbnailUri = blob.Uri.ToString().Replace("/images/", "/thumbnails/"),
						Caption = caption,
						//Tag = tags
					});
				}
			}
			ViewBag.Blobs = blobs.ToArray();
			return View();
		}

		public ActionResult About()
		{
			ViewBag.Message = "Here is a description about the IGWS Image Scanner.";

			return View();
		}

		public ActionResult Contact()
		{
			ViewBag.Message = "Contact us for more information.";

			return View();
		}
	}
}