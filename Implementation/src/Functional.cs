﻿//*************************************************************************************************
//* (C) ColorfulSoft corp., 2021 - 2022. All Rights reserved.
//*************************************************************************************************

using System;
using System.Numerics;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace ColorfulSoft.DeOldify
{

  /// <summary>
  /// Contains various operations on tensors.
  /// </summary>
  internal static unsafe class Functional
  {

    /// <summary>
    /// Two-dimensional averaging pooling.
    /// </summary>
    /// <param name="x">Input data.</param>
    /// <param name="kernelH">Kernel's height.</param>
    /// <param name="kernelW">Kenrel's width</param>
    /// <param name="strideY">Stride by "y".</param>
    /// <param name="strideX">Stride by "x".</param>
    /// <param name="paddingY">Padding by height.</param>
    /// <param name="paddingX">Padding by width.</param>
    /// <param name="dilationY">Dilation by height.</param>
    /// <param name="dilationX">Dilation by width.</param>
    /// <returns>Tensor.</returns>
    public static Tensor AvgPool2d(Tensor x,
                                   int kernelH,
                                   int kernelW,
                                   int strideY,
                                   int strideX,
                                   int paddingY,
                                   int paddingX,
                                   int dilationY,
                                   int dilationX)
    {
      int x_width = x.Shape[2];
      int x_height = x.Shape[1];
      int x_channel = x.Shape[0];
      int y_width = (x_width + 2 * paddingX - dilationX * (kernelW - 1) - 1) / strideX + 1;
      int y_height = (x_height + 2 * paddingY - dilationY * (kernelH - 1) - 1) / strideY + 1;
      int y_channel = x_channel;
      var y = new Tensor(y_channel, y_height, y_width);
      var px = x.Data;
      var py = y.Data;
      var winsize = (float)(kernelW * kernelH);
      Parallel.For(0, x_channel, (int c) =>
      {
        for (int ox = 0; ox < y_width; ++ox)
        {
          var ix_ = ox * strideX - paddingX;
          for (int oy = 0; oy < y_height; ++oy)
          {
            var iy_ = oy * strideY - paddingY;
            var mean = 0f;
            for (int fx = 0; fx < kernelW; ++fx)
            {
              var ix = ix_ + fx * dilationX;
              if ((ix >= x_width) || (ix < 0))
              {
                continue;
              }
              for (int fy = 0; fy < kernelH; ++fy)
              {
                var iy = iy_ + fy * dilationY;
                if ((iy >= x_height) || (iy < 0))
                {
                  continue;
                }
                mean += px[(c * x_height + iy) * x_width + ix];
              }
            }
            py[(c * y_height + oy) * y_width + ox] = mean / winsize;
          }
        }
      });
      return y;
    }

    /// <summary>
    /// BatchNorm2d
    /// </summary>
    /// <param name="x">Input data.</param>
    /// <param name="mean">Mean vector.</param>
    /// <param name="var">Variance vector.</param>
    /// <param name="weight">Weight vector.</param>
    /// <param name="bias">Bias vector.</param>
    /// <returns>Tensor.</returns>
    public static Tensor BatchNorm2d_(Tensor x, Tensor mean, Tensor @var, Tensor weight, Tensor bias)
    {
      var hw = x.Shape[1] * x.Shape[2];
      float* pmean = mean.Data;
      float* pvar = @var.Data;
      float* pw = weight.Data;
      float* pb = bias.Data;
      for (float* px_ = x.Data; px_ < (x.Data + x.Numel); px_ += hw, ++pmean, ++pvar, ++pw, ++pb)
      {
        for (float* px = px_; px < (px_ + hw); ++px)
        {
          *px = (*px - *pmean) / *pvar * *pw + *pb;
        }
      }
      return x;
    }

    // (C) Gleb S. Brykin, 2022
    // Gleb S. Brykin: Patch2Vec: a simple and efficient convolution algorithm for mobile neural networks, WNNA-2022 poster, 2022
    /// <summary>
    /// Conv2d.
    /// </summary>
    /// <param name="x">Input tensor.</param>
    /// <param name="weight">Weight.</param>
    /// <param name="bias">Bias.</param>
    /// <param name="padY">Padding by top side.</param>
    /// <param name="padX">Padding by left side.</param>
    /// <param name="padH">Padding by bottom side.</param>
    /// <param name="padW">Padding by right side.</param>
    /// <param name="strideY">Stride by "y".</param>
    /// <param name="strideX">Stride by "x".</param>
    /// <param name="dilationY">Stride by "y".</param>
    /// <param name="dilationX">Stride by "x".</param>
    /// <param name="group">Groups.</param>
    /// <returns>Tensor.</returns>
    public static Tensor Conv2d(Tensor x,
                                Tensor weight,
                                Tensor bias,
                                int padY,
                                int padX,
                                int padH,
                                int padW,
                                int strideY,
                                int strideX,
                                int dilationY,
                                int dilationX,
                                int group)
    {
      int srcC = x.Shape[0];
      int srcH = x.Shape[1];
      int srcW = x.Shape[2];
      int kernelY = weight.Shape[2];
      int kernelX = weight.Shape[3];
      int dstC = weight.Shape[0];
      int dstH = (srcH + padY + padH - (dilationY * (kernelY - 1) + 1)) / strideY + 1;
      int dstW = (srcW + padX + padW - (dilationX * (kernelX - 1) + 1)) / strideX + 1;
      var y = new Tensor(dstC, dstH, dstW);
      // Pointers
      var dst = y.Data;
      var pweight = weight.Data;
      var src = x.Data;
      dstC = dstC / group;
      srcC = srcC / group;
      int weight_base = srcC * kernelY * kernelX;
      int buf_size = srcC * kernelY * kernelX * sizeof(float);
      if (bias != null)
      {
        for (int g = 0; g < group; ++g)
        {
          var src_base1 = g * srcC;
          var dst_base1 = g * dstC;
          var bias_biased = bias.Data + g * dstC;
          Parallel.For(0, dstH, (int dy) =>
          {
            var buffer = (float*)Marshal.AllocHGlobal(buf_size).ToPointer();
            var sy1 = dy * strideY - padY;
            for (int dx = 0; dx < dstW; ++dx)
            {
              var sx1 = dx * strideX - padX;
              var buf = buffer;
              for (int sc = 0; sc < srcC; ++sc)
              {
                var src_base2 = (src_base1 + sc) * srcH;
                for (int ky = 0; ky < kernelY; ++ky)
                {
                  int sy = sy1 + ky * dilationY;
                  if ((sy < 0) || (sy >= srcH))
                  {
                    for (int kx = 0; kx < kernelX; ++kx)
                    {
                      *buf++ = 0;
                    }
                    continue;
                  }
                  var src_biased = src + (src_base2 + sy) * srcW;
                  for (int kx = 0; kx < kernelX; ++kx)
                  {
                    int sx = sx1 + kx * dilationX;
                    if ((sx >= 0) && (sx < srcW))
                    {
                      *buf++ = src_biased[sx];
                    }
                    else
                    {
                      *buf++ = 0;
                    }
                  }
                }
              }
              var dst_biased = dst + dx;
              for (int dc = 0; dc < dstC; ++dc)
              {
                float sum = 0;
                var w = pweight + (g * dstC + dc) * weight_base;
#if simd
                                    var buffer_vec = (Vector4*)buffer;
                                    var w_vec = (Vector4*)w;
                                    var result = new Vector4(0f);
                                    int m = 0;
                                    for(; m < weight_base / 4; ++m)
                                    {
                                        result += *buffer_vec++ * *w_vec++;
                                    }
                                    sum = result.X + result.Y + result.Z + result.W;
                                    m *= 4;
                                    for(; m < weight_base; ++m)
                                    {
                                        sum += buffer[m] * w[m];
                                    }
#else
                for (int m = 0; m < weight_base; ++m)
                {
                  sum += buffer[m] * w[m];
                }
#endif
                dst_biased[((dst_base1 + dc) * dstH + dy) * dstW] = sum + bias_biased[dc];
              }
            }
            Marshal.FreeHGlobal((IntPtr)buffer);
          });
        }
      }
      else
      {
        for (int g = 0; g < group; ++g)
        {
          var src_base1 = g * srcC;
          var dst_base1 = g * dstC;
          Parallel.For(0, dstH, (int dy) =>
          {
            var buffer = (float*)Marshal.AllocHGlobal(buf_size).ToPointer();
            var sy1 = dy * strideY - padY;
            for (int dx = 0; dx < dstW; ++dx)
            {
              var sx1 = dx * strideX - padX;
              var buf = buffer;
              for (int sc = 0; sc < srcC; ++sc)
              {
                var src_base2 = (src_base1 + sc) * srcH;
                for (int ky = 0; ky < kernelY; ++ky)
                {
                  int sy = sy1 + ky * dilationY;
                  if ((sy < 0) || (sy >= srcH))
                  {
                    for (int kx = 0; kx < kernelX; ++kx)
                    {
                      *buf++ = 0;
                    }
                    continue;
                  }
                  var src_biased = src + (src_base2 + sy) * srcW;
                  for (int kx = 0; kx < kernelX; ++kx)
                  {
                    int sx = sx1 + kx * dilationX;
                    if ((sx >= 0) && (sx < srcW))
                    {
                      *buf++ = src_biased[sx];
                    }
                    else
                    {
                      *buf++ = 0;
                    }
                  }
                }
              }
              var dst_biased = dst + dx;
              for (int dc = 0; dc < dstC; ++dc)
              {
                float sum = 0;
                var w = pweight + (g * dstC + dc) * weight_base;
#if simd
                                    var buffer_vec = (Vector4*)buffer;
                                    var w_vec = (Vector4*)w;
                                    var result = new Vector4(0f);
                                    int m = 0;
                                    for(; m < weight_base / 4; ++m)
                                    {
                                        result += *buffer_vec++ * *w_vec++;
                                    }
                                    sum = result.X + result.Y + result.Z + result.W;
                                    m *= 4;
                                    for(; m < weight_base; ++m)
                                    {
                                        sum += buffer[m] * w[m];
                                    }
#else
                for (int m = 0; m < weight_base; ++m)
                {
                  sum += buffer[m] * w[m];
                }
#endif
                dst_biased[((dst_base1 + dc) * dstH + dy) * dstW] = sum;
              }
            }
            Marshal.FreeHGlobal((IntPtr)buffer);
          });
        }
      }
      return y;
    }

    /// <summary>
    /// Multiplies each element in tensor by specified scalar inplace.
    /// </summary>
    /// <param name="x">Input tensor.</param>
    /// <param name="s">Scalar.</param>
    /// <returns>Tensor.</returns>
    public static Tensor EltwiseMulScalar_(Tensor x, float s)
    {
      var px = x.Data;
      for (int i = 0; i < x.Numel; ++i)
      {
        px[i] *= s;
      }
      return x;
    }

    /// <summary>
    /// Matrix multiplication.
    /// </summary>
    /// <param name="a">Matrix A.</param>
    /// <param name="b">Matrix B.</param>
    /// <returns>Tensor.</returns>
    public static Tensor MatMul(Tensor a, Tensor b)
    {
      var aw = a.Shape[1];
      var ah = a.Shape[0];
      var bw = b.Shape[1];
      var bh = b.Shape[0];
      b = b.Transpose2d();
      var c = new Tensor(ah, bw);
      var pa = a.Data;
      var pb = b.Data;
      var pc = c.Data;
      Parallel.For(0, ah, (int i) =>
      {
        var pa_ = pa + aw * i;
        var pc_ = pc + i * bw;
        for (int j = 0; j < bw; j++)
        {
          var pb_ = pb + bh * j;
          var v = 0f;
          for (int k = 0; k < bh; k++)
          {
            v += pa_[k] * pb_[k];
          }
          pc_[j] = v;
        }
      });
      return c;
    }

    /// <summary>
    /// MaxPool2d.
    /// </summary>
    /// <param name="x">Input tensor.</param>
    /// <param name="kernelH">Kernel's height.</param>
    /// <param name="kernelW">Kernel's width.</param>
    /// <param name="strideY">Stride by "y".</param>
    /// <param name="strideX">Stride by "x".</param>
    /// <param name="paddingY">Padding by "y".</param>
    /// <param name="paddingX">Padding by "x".</param>
    /// <param name="dilationY">Padding by "y".</param>
    /// <param name="dilationX">Padding by "x".</param>
    /// <returns>Tensor.</returns>
    public static Tensor MaxPool2d(Tensor x,
                                   int kernelH,
                                   int kernelW,
                                   int strideY,
                                   int strideX,
                                   int paddingY,
                                   int paddingX,
                                   int dilationY,
                                   int dilationX)
    {
      int x_width = x.Shape[2];
      int x_height = x.Shape[1];
      int x_channel = x.Shape[0];
      int y_width = (x_width + 2 * paddingX - dilationX * (kernelW - 1) - 1) / strideX + 1;
      int y_height = (x_height + 2 * paddingY - dilationY * (kernelH - 1) - 1) / strideY + 1;
      int y_channel = x_channel;
      var y = new Tensor(y_channel, y_height, y_width);
      var px = x.Data;
      var py = y.Data;
      Parallel.For(0, x_channel, (int c) =>
      {
        for (int ox = 0; ox < y_width; ++ox)
        {
          var ix_ = ox * strideX - paddingX;
          for (int oy = 0; oy < y_height; ++oy)
          {
            var iy_ = oy * strideY - paddingY;
            var max = float.MinValue;
            for (int fx = 0; fx < kernelW; ++fx)
            {
              var ix = ix_ + fx * dilationX;
              if ((ix >= x_width) || (ix < 0))
              {
                continue;
              }
              for (int fy = 0; fy < kernelH; ++fy)
              {
                var iy = iy_ + fy * dilationY;
                if ((iy >= x_height) || (iy < 0))
                {
                  continue;
                }
                var v = px[(c * x_height + iy) * x_width + ix];
                if (v > max)
                {
                  max = v;
                }
              }
            }
            py[(c * y_height + oy) * y_width + ox] = max;
          }
        }
      });
      return y;
    }

    /// <summary>
    /// PixelShuffle (scale_factor = 2).
    /// </summary>
    /// <param name="x">Input tensor.</param>
    /// <returns>Tensor.</returns>
    public static Tensor PixelShuffle(Tensor x)
    {
      var x_depth = x.Shape[0];
      var x_height = x.Shape[1];
      var x_width = x.Shape[2];
      var y = new Tensor(x.Shape[0] / 4, x.Shape[1] * 2, x.Shape[2] * 2);
      var y_depth = y.Shape[0];
      var y_height = y.Shape[1];
      var y_width = y.Shape[2];
      var py = y.Data;
      var px = x.Data;
      for (int od = 0; od < y_depth; ++od)
      {
        var id = od * 4;
        for (int oy = 0; oy < y_height; oy += 2)
        {
          var iy = oy / 2;
          for (int ox = 0; ox < y_width; ox += 2)
          {
            var ix = ox / 2;
            py[(od * y_height + oy) * y_width + ox] = px[(id * x_height + iy) * x_width + ix];
            py[(od * y_height + oy) * y_width + ox + 1] = px[((id + 1) * x_height + iy) * x_width + ix];
            py[(od * y_height + oy + 1) * y_width + ox] = px[((id + 2) * x_height + iy) * x_width + ix];
            py[(od * y_height + oy + 1) * y_width + ox + 1] = px[((id + 3) * x_height + iy) * x_width + ix];
          }
        }
      }
      return y;
    }

    /// <summary>
    /// Inplace unsafe elementwise sum.
    /// </summary>
    /// <param name="a">Tensor A.</param>
    /// <param name="b">Tensor B.</param>
    /// <returns>Tensor.</returns>
    public static Tensor Plus_(Tensor a, Tensor b)
    {
      var pa = a.Data;
      for (float* pb = b.Data; pb < (b.Data + b.Numel); ++pa, ++pb)
      {
        *pa += *pb;
      }
      return a;
    }

    /// <summary>
    /// Inplace relu.
    /// </summary>
    /// <param name="x">Input tensor.</param>
    /// <returns>Tensor.</returns>
    public static Tensor ReLU_(Tensor x)
    {
      for (float* px = x.Data; px < (x.Data + x.Numel); ++px)
      {
        if (*px < 0)
        {
          *px = 0;
        }
      }
      return x;
    }

    /// <summary>
    /// Concatenation of two three-dimensional tensors in the zero dimension (in depth)
    /// with the circumcision of the larger tensor.
    /// </summary>
    /// <param name="a">Tensor A.</param>
    /// <param name="b">Tensor B.</param>
    /// <returns>Tensor.</returns>
    public static Tensor RestrictedCat2d(Tensor a, Tensor b)
    {
      var height = Math.Min(a.Shape[1], b.Shape[1]);
      var width = Math.Min(a.Shape[2], b.Shape[2]);
      var adepth = a.Shape[0];
      var bdepth = b.Shape[0];
      var aheight = a.Shape[1];
      var bheight = b.Shape[1];
      var awidth = a.Shape[2];
      var bwidth = b.Shape[2];
      var depth = adepth + bdepth;
      var c = new Tensor(depth, height, width);
      var pa = a.Data;
      var pb = b.Data;
      var pc = c.Data;
      for (int y = 0; y < height; ++y)
      {
        for (int x = 0; x < width; ++x)
        {
          int gd = 0;
          for (int d = 0; d < adepth; ++d, ++gd)
          {
            pc[(gd * height + y) * width + x] = pa[(d * aheight + y) * awidth + x];
          }
          for (int d = 0; d < bdepth; ++d, ++gd)
          {
            pc[(gd * height + y) * width + x] = pb[(d * bheight + y) * bwidth + x];
          }
        }
      }
      return c;
    }

    /// <summary>
    /// Inplace sigmoid.
    /// </summary>
    /// <param name="x">Input tensor.</param>
    /// <returns>Tensor.</returns>
    public static Tensor Sigmoid_(Tensor x)
    {
      for (float* px = x.Data; px < x.Data + x.Numel; ++px)
      {
        *px = 1f / (1f + (float)Math.Exp(-*px));
      }
      return x;
    }

    /// <summary>
    /// Two-dimentional softmax.
    /// </summary>
    /// <param name="input">Input tensor.</param>
    /// <returns>Tensor.</returns>
    public static Tensor Softmax2d(Tensor input)
    {
      var result = new Tensor(input.Shape[0], input.Shape[1]);
      var px = input.Data;
      var py = result.Data;
      var height = input.Shape[0];
      var width = input.Shape[1];
      for (int y = 0; y < height; ++y)
      {
        var amax = float.MinValue;
        for (int x = 0; x < width; ++x)
        {
          var v = px[y * width + x];
          if (amax < v)
          {
            amax = v;
          }
        }
        var sum = 0f;
        for (int x = 0; x < width; ++x)
        {
          var v = px[y * width + x];
          sum += (float)Math.Exp(v - amax);
        }
        for (int x = 0; x < width; ++x)
        {
          var v = px[y * width + x];
          py[y * width + x] = (float)(Math.Exp(v - amax) / sum);
        }
      }
      return result;
    }

  }

}
