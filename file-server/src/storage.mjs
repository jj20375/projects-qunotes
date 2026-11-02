/**
 * @file storage.mjs
 * 檔案本體儲存：本機檔案系統（開發）或 S3／MinIO（正式）。資料庫只存 metadata，不存檔案本體。
 *
 * 兩種 driver 介面相同：
 * - put(key, tempPath, contentType)：把已驗證的暫存檔移入儲存區
 * - remove(key)
 * - open(key) → { size, stream(range?) } 或 null（不存在）
 */
import { createReadStream } from 'node:fs';
import { mkdir, rename, rm, stat } from 'node:fs/promises';
import { dirname, resolve, sep } from 'node:path';
import { PassThrough } from 'node:stream';
import { CreateBucketCommand, DeleteObjectCommand, GetObjectCommand, HeadBucketCommand, HeadObjectCommand, PutObjectCommand, S3Client } from '@aws-sdk/client-s3';

/**
 * @param {{ driver: string, root: string, bucket: string, s3: { endpoint?: string, region: string, accessKey: string, secretKey: string } }} options
 */
export function createStorage(options) {
  return options.driver === 's3' ? createS3Storage(options) : createFileSystemStorage(options.root);
}

function createFileSystemStorage(root) {
  const objectsRoot = resolve(root, 'objects');

  /** storage key 由 API 產生（{companyId}/{yyyy}/{MM}/{fileId}），仍再檢查一次不能跳出根目錄。 */
  function localPath(key) {
    const path = resolve(objectsRoot, key);
    if (!path.startsWith(objectsRoot + sep)) throw new Error('儲存路徑不合法。');
    return path;
  }

  return {
    driver: 'filesystem',
    async put(key, tempPath) {
      const target = localPath(key);
      await mkdir(dirname(target), { recursive: true });
      await rename(tempPath, target);
    },
    async remove(key) {
      await rm(localPath(key), { force: true });
    },
    async open(key) {
      const path = localPath(key);
      try {
        const { size } = await stat(path);
        return { size, stream: (range) => createReadStream(path, range) };
      } catch (error) {
        if (error?.code === 'ENOENT') return null;
        throw error;
      }
    },
  };
}

function createS3Storage(options) {
  const client = new S3Client({
    endpoint: options.s3.endpoint,
    region: options.s3.region,
    forcePathStyle: true,
    credentials: { accessKeyId: options.s3.accessKey, secretAccessKey: options.s3.secretKey },
  });
  let bucketReady = false;

  async function ensureBucket() {
    if (bucketReady) return;
    try {
      await client.send(new HeadBucketCommand({ Bucket: options.bucket }));
    } catch {
      await client.send(new CreateBucketCommand({ Bucket: options.bucket }));
    }
    bucketReady = true;
  }

  return {
    driver: 's3',
    async put(key, tempPath, contentType) {
      await ensureBucket();
      const { size } = await stat(tempPath);
      await client.send(new PutObjectCommand({ Bucket: options.bucket, Key: key, Body: createReadStream(tempPath), ContentLength: size, ContentType: contentType }));
      await rm(tempPath, { force: true });
    },
    async remove(key) {
      await ensureBucket();
      await client.send(new DeleteObjectCommand({ Bucket: options.bucket, Key: key }));
    },
    async open(key) {
      await ensureBucket();
      let size;
      try {
        size = Number((await client.send(new HeadObjectCommand({ Bucket: options.bucket, Key: key }))).ContentLength ?? 0);
      } catch (error) {
        if (error?.name === 'NotFound' || error?.$metadata?.httpStatusCode === 404) return null;
        throw error;
      }
      return {
        size,
        stream(range) {
          // S3 回應是非同步取得，先回傳 PassThrough 讓呼叫端可以立即 pipe。
          const output = new PassThrough();
          client
            .send(new GetObjectCommand({ Bucket: options.bucket, Key: key, ...(range ? { Range: `bytes=${range.start}-${range.end}` } : {}) }))
            .then((object) => object.Body.pipe(output))
            .catch((error) => output.destroy(error));
          return output;
        },
      };
    },
  };
}
