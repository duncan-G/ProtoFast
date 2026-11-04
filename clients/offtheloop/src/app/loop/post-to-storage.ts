/**
 * Posts a file to a signed storage URL as `multipart/form-data`: the signed fields as given, the
 * file last, since storage stops reading at the file. XHR rather than fetch, for upload progress.
 */
export function postToStorage(
  postUrl: string,
  fields: Record<string, string>,
  file: File,
  onProgress: (fraction: number) => void,
): Promise<void> {
  return new Promise<void>((resolve, reject) => {
    const form = new FormData();
    for (const [name, value] of Object.entries(fields)) {
      form.append(name, value);
    }
    form.append('file', file);

    const xhr = new XMLHttpRequest();
    xhr.upload.addEventListener('progress', (event) => {
      if (event.lengthComputable) {
        onProgress(Math.min(1, event.loaded / event.total));
      }
    });
    xhr.addEventListener('load', () =>
      xhr.status >= 200 && xhr.status < 300 ? resolve() : reject(new Error(refusal(xhr))),
    );
    xhr.addEventListener('error', () =>
      reject(new Error('The upload failed. Check your connection and try again.')),
    );
    xhr.open('POST', postUrl);
    xhr.send(form);
  });
}

function refusal(xhr: XMLHttpRequest): string {
  const code = /<Code>([^<]+)<\/Code>/.exec(xhr.responseText ?? '')?.[1];
  switch (code) {
    case 'EntityTooLarge':
      return 'Storage refused the file as too large.';
    case 'AccessDenied':
      return 'The upload link expired. Try again.';
    default:
      return `Storage refused the upload (HTTP ${xhr.status}).`;
  }
}
