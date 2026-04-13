using System;
using System.Linq;
using Amazon.S3.Transfer;
using Telerik.Sitefinity.Modules.Libraries.BlobStorage;
using Telerik.Sitefinity.BlobStorage;
using Amazon.S3;
using Amazon.S3.Model;
using System.Configuration;
using System.Collections.Specialized;
using System.IO;
using Amazon;
using System.Reflection;

namespace Telerik.Sitefinity.Amazon.BlobStorage
{
    /// <summary>
    /// This provider implements the logic for persisting BLOB data on Amazon S3 Storage.
    /// </summary>
    public class AmazonBlobStorageProvider : CloudBlobStorageProvider
    {
        #region Overridden Methods

        /// <summary>
        /// Initializes access to the remote storage.
        /// </summary>
        /// <param name="config">The collection of parameters (each by its name and value) of the current provider's configuration settings.</param>
        protected override void InitializeStorage(NameValueCollection config)
        {
            // Check if we should use IAM instance role instead of access keys
            if (config.Keys.Contains(UseIamInstanceRoleKey))
            {
                bool.TryParse(config[UseIamInstanceRoleKey], out this.useIamInstanceRole);
            }

            // Only validate access keys if not using IAM instance role
            if (!this.useIamInstanceRole)
            {
                this.accessKeyId = config[AccessKeyIdKey].Trim();
                if (String.IsNullOrEmpty(this.accessKeyId))
                    throw new ConfigurationException("'{0}' is required.".Arrange(AccessKeyIdKey));

                this.secretKey = config[SecretKeyKey].Trim();
                if (String.IsNullOrEmpty(this.secretKey))
                    throw new ConfigurationException("'{0}' is required.".Arrange(SecretKeyKey));
            }

            this.bucketName = config[BucketNameKey].Trim();
            if (String.IsNullOrEmpty(this.bucketName))
                throw new ConfigurationException("'{0}' is required.".Arrange(BucketNameKey));

            string regionEndpointString = config[RegionEndpointKey].Trim();
            var endpointField = typeof(RegionEndpoint).GetField(regionEndpointString, BindingFlags.Static | BindingFlags.Public);
            if ((string.IsNullOrWhiteSpace(regionEndpointString)) || (endpointField == null))
                throw new ConfigurationException("'{0}' is required.".Arrange(RegionEndpointKey));

            var regionEndpoint = (RegionEndpoint)endpointField.GetValue(null);

            // Initialize TransferUtility based on authentication method
            if (this.useIamInstanceRole)
            {
                // Use IAM instance role - AWS SDK will automatically use EC2/ECS instance credentials
                this.transferUtility = new TransferUtility(regionEndpoint);
            }
            else
            {
                // Use explicit access keys
                this.transferUtility = new TransferUtility(accessKeyId, secretKey, regionEndpoint);
            }

            // Optional key prefix for organizing files in subdirectories
            this.keyPrefix = string.Empty;
            if (config.Keys.Contains(KeyPrefixKey))
            {
                var keyPrefixValue = config[KeyPrefixKey];
                if (!String.IsNullOrEmpty(keyPrefixValue))
                {
                    this.keyPrefix = keyPrefixValue.Trim().TrimEnd('/') + "/";
                }
            }

            this.urlScheme = this.bucketName.Contains('.') ? Http : Https;
            if (config.Keys.Contains(UrlSchemeKey))
            {
                var urlSchemeValue = config[UrlSchemeKey];
                if (!String.IsNullOrEmpty(urlSchemeValue))
                {
                    urlSchemeValue = urlSchemeValue.ToLower();
                    if (urlSchemeValue == Http || urlSchemeValue == Https)
                    {
                        this.urlScheme = urlSchemeValue;
                    }
                }
            }

            this.serviceUrl = string.Concat(this.urlScheme, "://", this.bucketName, ".s3.amazonaws.com/");
        }

        /// <summary>
        /// Generates full CDN url from where a resource can be retrieved.
        /// </summary>
        /// <param name="cdnUrl">The base CDN url.</param>
        /// <param name="contentUrl">Relative url where the content can be found.</param>
        /// <returns>The full CDN url.</returns>
        protected override string GetCdnUrl(string cdnUrl, string contentUrl, params char[] urlDelimiters)
        {
            var url = new string[]
            {
                cdnUrl,
                contentUrl
            }
            .Select(x => x.Trim('/'))
            .Aggregate((x, y) => x + "/" + y);

            return string.Concat(this.urlScheme, "://", url);
        }

        /// <summary>
        /// Resolves the content item's external URL on the remote blob storage.
        /// </summary>
        /// <param name="content">Descriptor of the item on the remote blob storage for which to retrieve the URL.</param>
        /// <returns>The resolved content item's external URL on the remote blob storage.</returns>
        public override string GetItemUrl(IBlobContentLocation content)
        {
            return string.Concat(this.serviceUrl, this.keyPrefix, content.FilePath);
        }

        /// <summary>
        /// Copies the source content to the specified destination on the remote blob storage.
        /// </summary>
        /// <param name="source">Descriptor of the source item on the remote blob storage.</param>
        /// <param name="destination">Descriptor of the destination item on the remote blob storage.</param>
        public override void Copy(IBlobContentLocation source, IBlobContentLocation destination)
        {
            var request = new CopyObjectRequest()
            {
                SourceBucket = this.bucketName,
                SourceKey = this.keyPrefix + source.FilePath,
                DestinationBucket = this.bucketName,
                DestinationKey = this.keyPrefix + destination.FilePath
            };

            if(!this.useIamInstanceRole)
            {
                request.CannedACL = S3CannedACL.PublicRead;
            }

            request.Metadata.Add(nameof(IBlobContent.FileId).ToLower(), source.FileId.ToString());

            transferUtility.S3Client.CopyObject(request);
        }

        /// <summary>
        /// Sets the properties, like cacheControl, content type, etc.
        /// </summary>
        /// <param name="location">Descriptor of the item on the remote blob storage.</param>
        /// <param name="properties">The properties to set.</param>
        public override void SetProperties(IBlobContentLocation location, IBlobProperties properties)
        {
            //No properties to set by default
            var req = new CopyObjectRequest()
            {
                MetadataDirective = S3MetadataDirective.REPLACE,
                SourceBucket = this.bucketName,
                SourceKey = this.keyPrefix + location.FilePath,
                DestinationBucket = this.bucketName,
                DestinationKey = this.keyPrefix + location.FilePath
            };

            if(!this.useIamInstanceRole)
            {
                req.CannedACL = S3CannedACL.PublicRead;
            }

            req.Headers.CacheControl = properties.CacheControl;
            req.Headers.ContentType = properties.ContentType;
            req.Metadata.Add(nameof(IBlobContent.FileId).ToLowerInvariant(), location.FileId.ToString());

            transferUtility.S3Client.CopyObject(req);
        }

        /// <summary>
        /// Gets the content type, cache control settings, etc. of a blob.
        /// </summary>
        /// <param name="location">Descriptor of the item on the remote blob storage.</param>
        /// <returns>The retrieved properties.</returns>
        public override IBlobProperties GetProperties(IBlobContentLocation location)
        {
            var request = new GetObjectRequest()
            {
                BucketName = this.bucketName,
                Key = this.keyPrefix + location.FilePath
            };
            GetObjectResponse response = transferUtility.S3Client.GetObject(request);

            return new BlobProperties
            {
                ContentType = response.Headers["Content-Type"],
                CacheControl = response.Headers["Cache-Control"],
            };
        }

        /// <summary>
        /// Uploads the specified content item to the remote blob storage.
        /// </summary>
        /// <param name="content">Descriptor of the item on the remote blob storage.</param>
        /// <param name="source">The source item's content stream.</param>
        /// <param name="bufferSize">Size of the upload buffer.</param>
        /// <returns>The length of the uploaded stream.</returns>
        public override long Upload(IBlobContent content, Stream source, int bufferSize)
        {
            var request = new TransferUtilityUploadRequest()
            {
                BucketName = this.bucketName,
                Key = this.keyPrefix + content.FilePath,
                PartSize = bufferSize,
                ContentType = content.MimeType
            };

            if(!this.useIamInstanceRole)
            {
                request.CannedACL = S3CannedACL.PublicRead;
            }

            //get it before the upload, because afterwards the stream is closed already
            long sourceLength = source.Length;
            using (MemoryStream str = new MemoryStream())
            {
                source.CopyTo(str);
                request.InputStream = str;

                this.transferUtility.Upload(request);
            }
            return sourceLength;
        }

        /// <summary>
        /// Gets the upload stream for a specific content.
        /// </summary>
        /// <param name="content">Descriptor of the item on the remote blob storage.</param>
        /// <returns>The upload stream for a specific content.</returns>
        public override Stream GetUploadStream(IBlobContent content)
        {
            throw new NotSupportedException("GetUploadStream() is not supported. Override Upload method.");
        }

        /// <summary>
        /// Gets the download stream for a specific content..
        /// </summary>
        /// <param name="content">Descriptor of the item on the remote blob storage.</param>
        /// <returns>The binary stream of the content item.</returns>
        public override Stream GetDownloadStream(IBlobContent content)
        {
            TransferUtilityOpenStreamRequest request = new TransferUtilityOpenStreamRequest()
            {
                BucketName = this.bucketName,
                Key = this.keyPrefix + content.FilePath
            };
            var stream = this.transferUtility.OpenStream(request);
            return stream;
        }

        /// <summary>
        /// Deletes the blob item stored under the specified blob location
        /// </summary>
        /// <param name="location">Descriptor of the item on the remote blob storage.</param>
        public override void Delete(IBlobContentLocation location)
        {
            bool shouldDelete = true;
            try
            {
                GetObjectMetadataResponse response = transferUtility.S3Client.GetObjectMetadata(this.bucketName, this.keyPrefix + location.FilePath);
                string s3BlobFileId = response.Metadata[$"{metadataKeyPrefix}{nameof(IBlobContentLocation.FileId).ToLower()}"];
                string blobContentLocationId = location.FileId.ToString();
                if (!string.IsNullOrEmpty(s3BlobFileId) && !s3BlobFileId.Equals(blobContentLocationId))
                {
                    shouldDelete = false;
                }
            }
            catch
            {
                // do nothing and send delete request
            }
            finally
            {
                if (shouldDelete)
                {
                    var request = new DeleteObjectRequest()
                    {
                        BucketName = this.bucketName,
                        Key = this.keyPrefix + location.FilePath
                    };
                    transferUtility.S3Client.DeleteObject(request);
                }
            }
        }

        /// <summary>
        /// Determines whether a blob item under the specified location exists.
        /// </summary>
        /// <param name="location">Descriptor of the item on the remote blob storage.</param>
        /// <returns>True if the item exists, otherwise - false</returns>
        public override bool BlobExists(IBlobContentLocation location)
        {
            var request = new GetObjectRequest()
            {
                BucketName = this.bucketName,
                Key = this.keyPrefix + location.FilePath
            };
            try
            {
                var response = transferUtility.S3Client.GetObject(request);
                return true;
            }
            catch (AmazonS3Exception err)
            {
            }
            return false;
        }

        // <inheritdoc/>
        public override bool TestConnection(out Exception error)
        {
            try
            {
                ListObjectsRequest request = new ListObjectsRequest();
                request.BucketName = this.bucketName;

                ListObjectsResponse response = transferUtility.S3Client.ListObjects(request);
                if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
                {
                    error = new Exception(string.Format("Unexpected status code when loading objects from a bucket: {0}", response.HttpStatusCode));
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }

            error = null;
            return true;
        }

        #endregion

        #region Properties

        public const string AccessKeyIdKey = "accessKeyId";
        public const string SecretKeyKey = "secretKey";
        public const string BucketNameKey = "bucketName";
        public const string RegionEndpointKey = "regionEndpoint";
        public const string UrlSchemeKey = "urlScheme";
        public const string UseIamInstanceRoleKey = "useIamInstanceRole";
        public const string KeyPrefixKey = "keyPrefix";

        #endregion

        #region Fields

        private string accessKeyId = "";
        private string secretKey = "";
        private string bucketName = "";
        private string serviceUrl = "";
        private string urlScheme = "";
        private bool useIamInstanceRole = false;
        private string keyPrefix = "";
        TransferUtility transferUtility;
        private const string Http = "http";
        private const string Https = "https";
        private const string metadataKeyPrefix = "x-amz-meta-";

        #endregion
    }
}
