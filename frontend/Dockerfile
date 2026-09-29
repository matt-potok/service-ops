FROM node:24.18.0-bookworm-slim
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-fund
COPY . .
EXPOSE 5173
CMD ["sh", "-c", "npm ci --no-fund && npm run dev"]
