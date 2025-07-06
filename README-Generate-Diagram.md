# Generate Cross-Provider Customer Optimization Flow Diagram

This guide provides multiple ways to generate a downloadable JPG image of the Cross-Provider Customer Optimization Flow diagram.

## 🎯 Quick Options for Downloadable JPG

### Option 1: Use Python Script (Recommended)

**Requirements:**
```bash
pip install graphviz cairosvg pillow
```

**Steps:**
1. Run the Python script:
```bash
python generate_cross_provider_diagram.py
```

2. The script will create `CrossProviderOptimizationFlow.jpg` in the current directory
3. Download and use the JPG file as needed

### Option 2: Online Graphviz Tools

**Steps:**
1. Copy the content from `Cross-Provider-Optimization-Graphviz.dot`
2. Go to any online Graphviz editor:
   - https://dreampuf.github.io/GraphvizOnline/
   - https://edotor.net/
   - https://magjac.com/graphviz-visual-editor/

3. Paste the DOT code
4. Export as SVG/PNG/JPG and download

### Option 3: Local Graphviz Installation

**Install Graphviz:**
- **Windows:** Download from https://graphviz.org/download/
- **Mac:** `brew install graphviz`
- **Linux:** `sudo apt-get install graphviz` or `sudo yum install graphviz`

**Generate JPG:**
```bash
dot -Tjpg Cross-Provider-Optimization-Graphviz.dot -o CrossProviderOptimizationFlow.jpg
```

### Option 4: VS Code Extension

1. Install "Graphviz (dot) language support for Visual Studio Code"
2. Open the `.dot` file
3. Right-click → "Open Preview"
4. Export/Save as image

## 📋 Diagram Features

The generated JPG will include:
- 🔴 **Red boxes:** Critical triggers and execution processes
- 🔵 **Blue boxes:** Management and control processes  
- 🟢 **Green boxes:** Discovery and cleanup processes
- 🟡 **Yellow boxes:** Validation processes
- 🟣 **Purple boxes:** Generation and reporting processes

## 🎨 Color Legend

| Color | Purpose | Components |
|-------|---------|------------|
| 🔴 Red | Critical Triggers & Execution | AMOP 2.0 Trigger, Optimization Execution |
| 🔵 Blue | Management & Control | Session Management, Queue Creation, Result Compilation |
| 🟢 Green | Discovery & Cleanup | Rate Plan Discovery, Processing Cleanup |
| 🟡 Yellow | Validation | Customer Validation |
| 🟣 Purple | Generation & Reporting | Rate Pool Generation, Email & Reporting |

## 📁 Output Files

After running any method, you'll get:
- **CrossProviderOptimizationFlow.jpg** - High-quality downloadable JPG (recommended)
- **CrossProviderOptimizationFlow.png** - PNG format (if needed)
- **CrossProviderOptimizationFlow.svg** - Vector format (scalable)

## 🔗 Usage

The generated JPG can be used for:
- Technical documentation
- Presentations
- Architecture reviews
- System design discussions
- Training materials

## 📞 Troubleshooting

**Common Issues:**

1. **"graphviz command not found"**
   - Install Graphviz system package (see Option 3)

2. **"Module not found" errors**
   - Install Python packages: `pip install graphviz cairosvg pillow`

3. **Font rendering issues**
   - The diagram uses Helvetica font, fallback fonts will be used if not available

4. **Large file size**
   - The JPG is optimized for quality, typically 100-500KB

## 🎉 Success!

Once generated, you'll have a high-quality, color-coded Cross-Provider Customer Optimization Flow diagram that you can download, share, and use in any document or presentation.