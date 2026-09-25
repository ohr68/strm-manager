import { defineConfig } from 'vite';
import { resolve } from 'node:path';

export default defineConfig({
    build: {
        lib: {
            entry: resolve(__dirname, 'src/bootstrap.ts'),
            name: 'StrmManagerJellyfin',
            formats: ['iife'],
            fileName: () => 'strm-manager.js',
        },
        outDir: 'dist',
        emptyOutDir: true,
    },
});