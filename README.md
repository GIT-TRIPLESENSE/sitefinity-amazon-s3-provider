# Sitefinity's Amazon S3 Blob Storage Provider (with IAM Role Support)

**This is a fork of [Sitefinity's Amazon S3 Provider](https://github.com/Sitefinity/amazon-s3-provider) with IAM role authentication support for AWS ECS/EC2 environments.**

## Features

- ✅ **IAM Role Authentication**: Use ECS task roles or EC2 instance profiles instead of hardcoded access keys
- ✅ **Key Prefix Support**: Organize files in S3 subdirectories for multi-environment setups
- ✅ **Backward Compatible**: All existing configurations continue to work

Sitefinity's Amazon S3 Blob Storage Provider is an implementation of a cloud blob storage provider, which stores the binary blob data of Sitefinity's library items on Amazon's Simple Storage Service (S3).

Note that only the binary data of the items in a library are stored on the remote blob storage. Sitefinity still manages its logical items by library with their regular meta-data properties (title, description etc) in its own database.

## New Configuration Parameters

### `useIamInstanceRole` (optional, default: `false`)

When set to `true`, the provider uses the IAM role attached to your ECS task or EC2 instance instead of requiring `accessKeyId` and `secretKey`.

**Example:**

```xml
<add key="useIamInstanceRole" value="true" />
```

**Important:** When using IAM roles, ensure your ECS task role or EC2 instance profile has the necessary S3 permissions.

### `keyPrefix` (optional)

Allows storing files in a subdirectory within your S3 bucket. Useful for:

- Sharing one S3 bucket across multiple environments (dev/staging/prod)
- Organizing Sitefinity files separately from other bucket contents

**Example:**

```xml
<add key="keyPrefix" value="production/sitefinity" />
```

## Configuration Examples

### Using IAM Role (Recommended for ECS/EC2)

```xml
<blobStorageProviders>
  <add name="AmazonBlobStorage"
       type="Telerik.Sitefinity.Amazon.BlobStorage.AmazonBlobStorageProvider"
       bucketName="my-sitefinity-bucket"
       regionEndpoint="USEast1"
       useIamInstanceRole="true"
       keyPrefix="production" />
</blobStorageProviders>
```

### Using Access Keys (Traditional)

```xml
<blobStorageProviders>
  <add name="AmazonBlobStorage"
       type="Telerik.Sitefinity.Amazon.BlobStorage.AmazonBlobStorageProvider"
       accessKeyId="YOUR_ACCESS_KEY"
       secretKey="YOUR_SECRET_KEY"
       bucketName="my-sitefinity-bucket"
       regionEndpoint="USEast1" />
</blobStorageProviders>
```

## Credits

This fork applies [PR #9](https://github.com/Sitefinity/amazon-s3-provider/pull/9) by [@BenWolstencroft](https://github.com/BenWolstencroft).

For full documentation on the base provider, please refer to the [upstream Wiki](https://github.com/Sitefinity/amazon-s3-provider/wiki).
