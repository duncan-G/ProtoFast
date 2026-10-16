/** Hands the file to the browser as a download under its own name. */
export function saveFile(file: File): void {
  const url = URL.createObjectURL(file);
  const link = document.createElement('a');
  link.href = url;
  link.download = file.name;
  link.click();
  // Some browsers only start reading the URL after the click returns.
  setTimeout(() => URL.revokeObjectURL(url));
}
