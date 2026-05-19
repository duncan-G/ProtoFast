const serverUrl = process.env.SERVER_URL || 'http://localhost:8080';

export default {
  '/auth': { target: serverUrl, secure: false, changeOrigin: true },
  '/payments': { target: serverUrl, secure: false, changeOrigin: true },
  '/api': { target: serverUrl, secure: false, changeOrigin: true },
};
