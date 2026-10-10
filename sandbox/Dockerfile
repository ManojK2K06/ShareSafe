FROM python:3.12-slim
# Image installation can use networking. Every agent execution uses --network none.
RUN apt-get update && apt-get install -y --no-install-recommends nodejs git \
    && rm -rf /var/lib/apt/lists/*
COPY workspace_tool.py /usr/local/lib/sharesafe/workspace_tool.py
ENV PYTHONDONTWRITEBYTECODE=1 HOME=/tmp
WORKDIR /workspace
CMD ["python", "--version"]
