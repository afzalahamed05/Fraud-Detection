FROM python:3.11-slim

RUN apt-get update && apt-get install -y --no-install-recommends default-jre-headless curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

RUN mkdir -p /opt/spark-jars && \
    curl -fsSL https://repo1.maven.org/maven2/org/postgresql/postgresql/42.7.3/postgresql-42.7.3.jar \
    -o /opt/spark-jars/postgresql-42.7.3.jar

COPY . .

CMD ["python", "analytics_job.py"]
