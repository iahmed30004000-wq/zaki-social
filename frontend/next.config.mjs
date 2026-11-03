/** @type {import('next').NextConfig} */
const nextConfig = {
  output: "standalone",
  poweredByHeader: false,
  eslint: { ignoreDuringBuilds: true },
};

export default nextConfig;
