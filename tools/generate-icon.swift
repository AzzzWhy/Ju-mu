import AppKit
import Foundation

// Recreates the green square / white 幕 mark used in Jumu's header.
// Run on macOS: swift tools/generate-icon.swift
let project = URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
let assets = project.appendingPathComponent("src/Scribe.Desktop/Assets", isDirectory: true)
try FileManager.default.createDirectory(at: assets, withIntermediateDirectories: true)

func png(_ size: Int) throws -> Data {
    guard let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size,
        bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
        colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0),
        let graphics = NSGraphicsContext(bitmapImageRep: bitmap) else { fatalError("Cannot draw icon") }
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = graphics
    graphics.imageInterpolation = .high
    NSColor.clear.setFill()
    NSRect(x: 0, y: 0, width: size, height: size).fill()
    let margin = CGFloat(size) * 0.035
    let square = NSRect(x: margin, y: margin, width: CGFloat(size)-2*margin, height: CGFloat(size)-2*margin)
    NSColor(calibratedRed: 35/255, green: 103/255, blue: 82/255, alpha: 1).setFill()
    NSBezierPath(roundedRect: square, xRadius: CGFloat(size)*0.19, yRadius: CGFloat(size)*0.19).fill()
    let font = NSFont(name: "PingFangSC-Semibold", size: CGFloat(size)*0.61)
        ?? NSFont.systemFont(ofSize: CGFloat(size)*0.61, weight: .bold)
    let title = "幕" as NSString
    let attributes: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: NSColor.white]
    let measured = title.size(withAttributes: attributes)
    let frame = NSRect(x: (CGFloat(size)-measured.width)/2, y: (CGFloat(size)-measured.height)/2 + CGFloat(size)*0.018,
        width: measured.width+2, height: measured.height+2)
    title.draw(in: frame, withAttributes: attributes)
    graphics.flushGraphics()
    NSGraphicsContext.restoreGraphicsState()
    guard let data = bitmap.representation(using: .png, properties: [:]) else { fatalError("Cannot encode PNG") }
    return data
}

func appendLE<T: FixedWidthInteger>(_ number: T, to data: inout Data) {
    withUnsafeBytes(of: number.littleEndian) { data.append(contentsOf: $0) }
}

let sizes = [16, 32, 48, 64, 128, 256, 512, 1024]
var images: [Int: Data] = [:]
for size in sizes { images[size] = try png(size) }
try images[512]!.write(to: assets.appendingPathComponent("Jumu.png"))

// ICO directory entries can contain PNG images on supported Windows versions.
let icoSizes = [16, 32, 48, 256]
var ico = Data()
appendLE(UInt16(0), to: &ico)
appendLE(UInt16(1), to: &ico)
appendLE(UInt16(icoSizes.count), to: &ico)
var offset = UInt32(6 + icoSizes.count*16)
for size in icoSizes {
    let image = images[size]!
    ico.append(UInt8(size == 256 ? 0 : size))
    ico.append(UInt8(size == 256 ? 0 : size))
    ico.append(contentsOf: [0, 0])
    appendLE(UInt16(1), to: &ico)
    appendLE(UInt16(32), to: &ico)
    appendLE(UInt32(image.count), to: &ico)
    appendLE(offset, to: &ico)
    offset += UInt32(image.count)
}
for size in icoSizes { ico.append(images[size]!) }
try ico.write(to: assets.appendingPathComponent("Jumu.ico"))

let iconset = FileManager.default.temporaryDirectory.appendingPathComponent("Jumu-\(UUID().uuidString).iconset", isDirectory: true)
try FileManager.default.createDirectory(at: iconset, withIntermediateDirectories: true)
for (name, size) in [("icon_16x16.png",16), ("icon_16x16@2x.png",32),
                     ("icon_32x32.png",32), ("icon_32x32@2x.png",64),
                     ("icon_128x128.png",128), ("icon_128x128@2x.png",256),
                     ("icon_256x256.png",256), ("icon_256x256@2x.png",512),
                     ("icon_512x512.png",512), ("icon_512x512@2x.png",1024)] {
    try images[size]!.write(to: iconset.appendingPathComponent(name))
}
let process = Process()
process.executableURL = URL(fileURLWithPath: "/usr/bin/iconutil")
process.arguments = ["-c", "icns", iconset.path, "-o", assets.appendingPathComponent("Jumu.icns").path]
try process.run()
process.waitUntilExit()
guard process.terminationStatus == 0 else { fatalError("iconutil failed") }
try FileManager.default.removeItem(at: iconset)
print("Generated Jumu.png, Jumu.ico, and Jumu.icns in \(assets.path)")
